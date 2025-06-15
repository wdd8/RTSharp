using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DynamicData;
using DynamicData.Binding;

using Microsoft.Extensions.DependencyInjection;

using NP.Ava.UniDockService;
using NP.Utilities;

using RTSharp.Core;
using RTSharp.Core.TorrentPolling;
using RTSharp.Core.Util;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Controls;
using RTSharp.ViewModels.Options;
using RTSharp.Views.Options;
using RTSharp.Views.TorrentListing;

using Serilog;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Torrent = RTSharp.Models.Torrent;

namespace RTSharp.ViewModels.TorrentListing
{
    public class DockTorrentListingViewModel : DockItemViewModel<TorrentListingViewModel> { }

    public partial class TorrentListingViewModel : ObservableObject, IContextPopulatedNotifyable, IDockable
    {
        public SourceList<Torrent> Torrents { get; } = TorrentPolling.Torrents;

        private SourceList<Torrent> _filteredTorrents { get; } = new();
        private ReadOnlyObservableCollection<Torrent> FilteredTorrents;
        private bool FilterApplied { get; set; }

        public FlatTreeDataGridSource<Torrent> Source { get; }

        public TreeDataGrid DataGrid { get; set; }

        SourceList<Torrent> CurrentlySelectedItems { get; } = new();

        [ObservableProperty]
        private string stringFilter;

        [ObservableProperty]
        private TemplatedControl[] labelsWithAdd;

        private GeneralTorrentInfoViewModel GeneralInfoViewModel { get; } = new();

        private TorrentFilesViewModel FilesViewModel { get; } = new();

        private TorrentPeersViewModel PeersViewModel { get; } = new();

        private TorrentTrackersViewModel TrackersViewModel { get; }

        private static readonly DrawingImage DefaultImage;

        private bool StartTorrentAllowed => CurrentlySelectedItems.Items.Any() && ((Torrent)CurrentlySelectedItems.Items[0]).InternalState != TORRENT_STATE.SEEDING;

        public string HeaderName => "Torrent listing";

        public Geometry? Icon => null;

        static TorrentListingViewModel()
        {
            DefaultImage = new DrawingImage() {
                Drawing = new GeometryDrawing() {
                    Geometry = FontAwesomeIcons.Get("fa-solid fa-globe"),
                    Brush = Brushes.White
                }
            };
        }

        private List<IDisposable> VMDisposables = new();
        private List<IDisposable> SelectionDisposables = new();

        private string LastSerialization { get; set; }

        public record ColumnSerializationData(string HeaderId, int DisplayIndex, bool IsVisible, ListSortDirection? Sorting, string Width);
        public record SerializationData(List<ColumnSerializationData> Columns);

        List<IColumn<Torrent>> OriginalColumns { get; }

        private class FormattableTextColumn<T> : TemplateColumn<Torrent>
            where T : IComparable<T>
        {
            public FormattableTextColumn(string Header, Expression<Func<Torrent, string>> FxStr, Func<Torrent, T> FxValue, GridLength? Width)
                : this(Header, FxStr, FxStr.Compile(), FxValue, Width)
            {
            }
            private FormattableTextColumn(string Header, Expression<Func<Torrent, string>> FxStr, Func<Torrent, string> FxStrCompiled, Func<Torrent, T> FxValue, GridLength? Width)
                : base(Header, new FuncDataTemplate<Torrent>(x => x is Torrent, x => x == null ? null : new TreeDataGridTextCell() { Value = FxStrCompiled(x) }, supportsRecycling: true), null, Width, new TemplateColumnOptions<Torrent> {
                    TextSearchValueSelector = x => FxStrCompiled(x),
                    CompareAscending = new Comparison<Torrent?>((a, b) => FxValue(a).CompareTo(FxValue(b))),
                    CompareDescending = new Comparison<Torrent?>((a, b) => -FxValue(a).CompareTo(FxValue(b))),
                })
            {
            }
        }

        event Action Sorted;

        public TorrentListingViewModel()
        {
            this.TrackersViewModel = new(this);

            using var scope = Core.ServiceProvider.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<Core.Config>();

            SerializationData? data = null;
            try {
                data = JsonSerializer.Deserialize<SerializationData>(config.UIState.Value.TorrentGridState)!;
            } catch { }

            LastSerialization = config.UIState.Value.TorrentGridState;

            var widths = data == null ? [] : data.Columns.ToDictionary(x => x.HeaderId, x => String.IsNullOrEmpty(x.Width) ? default : (GridLength?)GridLength.Parse(x.Width));

            var columnList = new List<IColumn<Torrent>>
            {
                new TextColumn<Torrent, string>("Connection", x => x.Owner.PluginInstance.PluginInstanceConfig.Name, width: widths.GetValueOrDefault("Connection")),
                new TextColumn<Torrent, string>("Name", x => x.Name, width: widths.GetValueOrDefault("Name"), options: new TextColumnOptions<Torrent> {
                    IsTextSearchEnabled = true
                }),
                new TextColumn<Torrent, string>("State", x => x.State, width: widths.GetValueOrDefault("State")),
                new FormattableTextColumn<ulong>("Size", x => Shared.Utils.Converters.GetSIDataSize(x.Size), x => x.Size, widths.GetValueOrDefault("Size")),
                new TemplateColumn<Torrent>("Done", "DoneCell", width: widths.GetValueOrDefault("Done"), options: new TemplateColumnOptions<Torrent> {
                    CompareAscending = new Comparison<Torrent?>((a, b) => a.Done.CompareTo(b.Done))
                }),
                new FormattableTextColumn<ulong>("Downloaded", x => Shared.Utils.Converters.GetSIDataSize(x.Downloaded), x => x.Downloaded, widths.GetValueOrDefault("Downloaded")),
                new FormattableTextColumn<ulong>("Completed", x => Shared.Utils.Converters.GetSIDataSize(x.CompletedSize), x => x.CompletedSize, widths.GetValueOrDefault("Completed")),
                new FormattableTextColumn<ulong>("Uploaded", x => Shared.Utils.Converters.GetSIDataSize(x.Uploaded), x => x.Uploaded, widths.GetValueOrDefault("Uploaded")),
                new FormattableTextColumn<ulong>("Remaining", x => Shared.Utils.Converters.GetSIDataSize(x.RemainingSize), x => x.RemainingSize, widths.GetValueOrDefault("Remaining")),
                new FormattableTextColumn<ulong>("DL Speed", x => Shared.Utils.Converters.GetSIDataSize(x.DLSpeed) + "/s", x => x.DLSpeed, widths.GetValueOrDefault("DL Speed")),
                new FormattableTextColumn<ulong>("UP Speed", x => Shared.Utils.Converters.GetSIDataSize(x.UPSpeed) + "/s", x => x.UPSpeed, widths.GetValueOrDefault("UP Speed")),
                new TextColumn<Torrent, string>("Peers", x => x.Peers.ToString(), width: widths.GetValueOrDefault("Peers")),
                new TextColumn<Torrent, string>("Seeders", x => x.Seeders.ToString(), width: widths.GetValueOrDefault("Seeders")),
                new TextColumn<Torrent, string>("Labels", x => String.Join(", ", x.Labels), width: widths.GetValueOrDefault("Labels")),
                new TextColumn<Torrent, string>("ETA", x => Shared.Utils.Converters.ToAgoString(x.ETA), width: widths.GetValueOrDefault("ETA")),
                new TextColumn<Torrent, string>("Created On", x => x.CreatedOnDate == null ? "N/A" : x.CreatedOnDate.Value.ToString(), width: widths.GetValueOrDefault("Created On")),
                new TextColumn<Torrent, string>("Added On", x => new Nullable<DateTime>(x.AddedOnDate).Value.ToString(), width: widths.GetValueOrDefault("Added On")),
                new TextColumn<Torrent, float>("Ratio", x => x.Ratio, options: new TextColumnOptions<Torrent> {
                    StringFormat = "{0:0.000}"
                }, width: widths.GetValueOrDefault("Ratio")),
                new TextColumn<Torrent, string>("Finished On", x => x.FinishedOnDate == null ? "N/A" : x.FinishedOnDate.Value.ToString(), width: widths.GetValueOrDefault("Finished On")),
                new TemplateColumn<Torrent>("Tracker", "TrackerCell", width: widths.GetValueOrDefault("Tracker")),
                new TextColumn<Torrent, string>("Priority", x => x.Priority, width: widths.GetValueOrDefault("Priority")),
            };
            OriginalColumns = [ ..columnList ];

            if (data.Columns.Count != 0) {
                columnList.Clear();
                foreach (var col in data.Columns.OrderBy(x => x.DisplayIndex)) {
                    if (!col.IsVisible)
                        continue;

                    var real = OriginalColumns.First(x => (string)x.Header == col.HeaderId);

                    if (col.Sorting != null)
                        real.SortDirection = col.Sorting.Value;

                    columnList.Add(real);
                }
            }

            App.RegisterOnExit($"{nameof(TorrentListingView)}_{nameof(DataGrid)}_{nameof(SaveGridState)}", () => SaveGridState());

            var comparerChanged = Observable.FromEvent(x => Sorted += x, x => { Sorted -= x; }).Select(x => {
                foreach (var col in Source.Columns) {
                    if (col.SortDirection == null)
                        continue;

                    if ((string)col.Header == "Name")
                        return SortExpressionComparer<Torrent>.Ascending(static x => x.Name);
                }
                return SortExpressionComparer<Torrent>.Descending(static x => x.UPSpeed);
            });
            VMDisposables.Add(_filteredTorrents.Connect().Bind(out FilteredTorrents).Subscribe());
            //.Sort(comparerChanged, SortOptions.None)

            Source = new FlatTreeDataGridSource<Torrent>(FilteredTorrents) {
                Columns = { columnList }
            };
            Source.RowSelection!.SingleSelect = false;
            Source.Sorted += Sorted;

            /*foreach (var col in Source.Columns) {
                if (col.SortDirection == null)
                    continue;

                ((ITreeDataGridSource)Source).SortBy(col, col.SortDirection.Value);
            }*/
        }

        private string GetState()
        {
            var nonExistentCounter = 0;
            return JsonSerializer.Serialize(new SerializationData(
                [.. OriginalColumns.Select((x, i) => {
                    var idx = -1;
                    for (var a = 0;a < DataGrid.Columns.Count;a++) {
                        if (DataGrid.Columns[a].Header.ToString() == x.Header.ToString()) {
                            idx = a;
                            break;
                        }
                    }
                    IColumn? col = null;
                    if (idx != -1)
                        col = DataGrid.Columns[idx];

                    return new ColumnSerializationData(x.Header.ToString()!, idx == -1 ? DataGrid.Columns.Count + ++nonExistentCounter : idx, col != null, col?.SortDirection ?? x.SortDirection, col?.Width.ToString() ?? x.Width.ToString());
                }) ]
            ));
        }

        public async ValueTask SaveGridState()
        {
            try {
                string? state = null;

                if (LastSerialization != GetState())
                    state = GetState();

                if (state != null) {
                    using var scope = Core.ServiceProvider.CreateScope();
                    var config = scope.ServiceProvider.GetRequiredService<Core.Config>();
                    config.UIState.Value.TorrentGridState = state;
                    await config.Rewrite();
                }
            } catch (Exception ex) {
                Log.Logger.Error(ex, "SaveGridState");
            }
        }

        ~TorrentListingViewModel()
        {
            foreach (var item in VMDisposables)
                item.Dispose();

            VMDisposables.Clear();
        }

        public void OnViewModelAttached(TorrentListingView View)
        {
            this.DataGrid = View.grid;

            DataGrid.PropertyChanged += (sender, e) => {
                if (e.Property.Name != nameof(DataGrid.Source))
                    return;

                _filteredTorrents.Clear();

                if (e.NewValue != null) {
                    _filteredTorrents.AddRange(Torrents.Items);
                    VMDisposables.Add(
                        Observable.FromEventPattern<TreeSelectionModelSelectionChangedEventArgs>(x => DataGrid.RowSelection.SelectionChanged += x, x => DataGrid.RowSelection.SelectionChanged -= x)
                        .Subscribe(x => {
                            CurrentlySelectedItems.Edit(i => {
                                i.Clear();
                                i.AddRange(x.EventArgs.SelectedItems.Cast<Torrent>());
                            });
                            CurrentlySelectedTorrents_CollectionChanged();
                        })
                    );
                }
            };
        }

        public void OnTorrentsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => EvaluateFilters(e);

        partial void OnStringFilterChanged(string value) => EvaluateFilters(null);

        [RelayCommand]
        public void ShowAddLabel()
        {
            ShowAddLabelDialog();
        }

        [RelayCommand]
        public void ShowOptions()
        {
            var optionsWindow = new OptionsWindow() {
                DataContext = new OptionsViewModel()
            };
            optionsWindow.Show();
        }

        private TemplatedControl[] GetLabelsWithAdd(string[] AllLabels)
        {
            var ret = new List<TemplatedControl>();

            if (CurrentlySelectedItems == null)
                return Array.Empty<TemplatedControl>();

            var current = CurrentlySelectedItems.Items.ToArray();

            foreach (var label in AllLabels) {
                int checkedCount = 0;
                foreach (var torrent in current) {
                    if (torrent.Labels.Contains(label)) {
                        checkedCount++;
                    }
                }

                ret.Add(new MenuItem {
                    Icon = new CheckBox() {
                        BorderThickness = new Avalonia.Thickness(0),
                        IsHitTestVisible = true,
                        Command = ToggleLabelCommand,
                        CommandParameter = (current, label, checkedCount != 0),
                        IsThreeState = true,
                        IsChecked = checkedCount == 0 ? false : (checkedCount == current.Length ? true : null)
                    },
                    Header = label
                });
            }

            if (ret.Count > 0)
                ret.Add(new Separator());

            ret.Add(new MenuItem {
                Command = ShowAddLabelCommand,
                Header = "Add..."
            });

            return [.. ret];
        }

        private void UpdateTorrentInChildViewModels(IReadOnlyList<Torrent>? NewTorrents)
        {
            if (NewTorrents?.Count != 1) {
                GeneralInfoViewModel.Torrent = null;
                FilesViewModel.Torrent = null;
                FilesViewModel.Files.Clear();
                PeersViewModel.Peers.Clear();
                TrackersViewModel.Trackers.Clear();
                return;
            }

            var newTorrent = NewTorrents[0];

            GeneralInfoViewModel.Torrent = newTorrent;
            FilesViewModel.Torrent = newTorrent;
        }

        private Task? TorrentTabsTasks;
        private SemaphoreSlim TorrentTabsSwitch = new(1, 1);
        private CancellationTokenSource? SelectionChange { get; set; }
        private async Task PollTorrentInfo(IReadOnlyList<Torrent>? NewTorrents)
        {
            try {
                await TorrentTabsSwitch.WaitAsync();
                var newTorrent = NewTorrents?.Count != 1 ? null : NewTorrents[0];

                if (SelectionChange == null)
                    SelectionChange = new CancellationTokenSource();
                else {
                    SelectionChange.Cancel();

                    var sw = Stopwatch.StartNew();
                    try {
                        if (TorrentTabsTasks != null) {
                            await TorrentTabsTasks;
                        }
                    } catch (TaskCanceledException) {
                    } catch (Exception ex) {
                        Log.Logger.Error(ex, "One of torrent tasks failed");
                    } finally {
                        TorrentTabsTasks = null;
                    }
                    if (sw.Elapsed > TimeSpan.FromMilliseconds(100))
                        Log.Logger.Warning($"Torrent tasks are taking too long to complete ({Shared.Utils.Converters.ToAgoString(sw.Elapsed)})");
                }

                if (newTorrent != null) {
                    SelectionChange = new CancellationTokenSource();

                    Debug.Assert(TorrentTabsTasks == null);

                    Log.Logger.Information("Starting tasks for: " + newTorrent.Name);
                    TorrentTabsTasks = Task.WhenAll(
                        FilesTasks(newTorrent, SelectionChange.Token),
                        PeersTasks(newTorrent, SelectionChange.Token),
                        TrackersTasks(newTorrent, SelectionChange.Token)
                    );
                }
            } finally {
                TorrentTabsSwitch.Release();
            }
        }

        private void EvaluateFilters(NotifyCollectionChangedEventArgs? e)
        {
            if (Torrents == null)
                return;

            this.OnPropertyChanging(nameof(_filteredTorrents));

            _filteredTorrents.Edit(torrents => {
                if (e.Action == NotifyCollectionChangedAction.Reset) {
                        torrents.Clear();
                        torrents.AddRange(Torrents.Items);
                } else {
                    if (e?.NewItems?.Count > 0) {
                        torrents.AddRange(e.NewItems.Cast<Torrent>());
                    }
                    if (e?.OldItems?.Count > 0) {
                        foreach (var item in e.OldItems) {
                            var idx = torrents.IndexOf(item);
                            if (idx != -1) {
                                if (e.Action == NotifyCollectionChangedAction.Remove)
                                    torrents.RemoveAt(idx);
                                else
                                    torrents[idx] = (Torrent)item;
                            }
                        }
                    }
                }

                if (String.IsNullOrEmpty(StringFilter) && FilterApplied) {
                    torrents.Clear();
                    torrents.AddRange(Torrents.Items);
                    FilterApplied = false;
                } else if (!String.IsNullOrEmpty(StringFilter)) {
                    var newTorrents = new ConcurrentBag<Torrent>();
                    Parallel.ForEach(Torrents.Items, x => {
                        if (x.Name.Contains(StringFilter, StringComparison.OrdinalIgnoreCase)) {
                            newTorrents.Add(x);
                        } else if (x.TrackerDisplayName != null && x.TrackerDisplayName.Contains(StringFilter, StringComparison.OrdinalIgnoreCase)) {
                            newTorrents.Add(x);
                        }
                    });
                    torrents.Clear();
                    torrents.AddRange(newTorrents);
                    FilterApplied = true;
                }
            });

            this.OnPropertyChanged(nameof(_filteredTorrents));

            SortList();
        }

        private void SortList()
        {
            foreach (var col in Source.Columns) {
                if (col.SortDirection == null)
                    continue;

                ((ITreeDataGridSource)Source).SortBy(col, col.SortDirection.Value);
            }
        }

        public void OnContextPopulated()
        {
            var observable = new ReadOnlyObservableCollection<Torrent>([]);
            VMDisposables.Add(Torrents.Connect().Bind(out observable).Subscribe());
            ((INotifyCollectionChanged)observable).CollectionChanged += OnTorrentsCollectionChanged;

            TorrentPolling.TorrentBatchChange -= SortList;
            TorrentPolling.TorrentBatchChange += SortList;

            VMDisposables.Add(TorrentPolling.AllLabelReferencesObservable.Subscribe(x => LabelsWithAdd = GetLabelsWithAdd(x)));

            this.OnPropertyChanging(nameof(_filteredTorrents));
            _filteredTorrents.AddRange(Torrents.Items);
            this.OnPropertyChanged(nameof(_filteredTorrents));
        }

        private void CurrentlySelectedTorrents_CollectionChanged()
        {
            foreach (var item in SelectionDisposables)
                item.Dispose();

            SelectionDisposables.Clear();

            UpdateTorrentInChildViewModels(this.CurrentlySelectedItems.Items);
            _ = PollTorrentInfo(this.CurrentlySelectedItems.Items);
            this.OnPropertyChanged(nameof(StartTorrentAllowed));
            this.OnPropertyChanged(nameof(CurrentlySelectedItems));
            StartTorrentsCommand.NotifyCanExecuteChanged();
            StopTorrentsCommand.NotifyCanExecuteChanged();
            PauseTorrentsCommand.NotifyCanExecuteChanged();
            ForceRecheckTorrentsCommand.NotifyCanExecuteChanged();
            ReannounceToAllTrackersCommand.NotifyCanExecuteChanged();
            MoveDownloadDirectoryCommand.NotifyCanExecuteChanged();
            RemoveTorrentsCommand.NotifyCanExecuteChanged();
            RemoveTorrentsAndDataCommand.NotifyCanExecuteChanged();
            GetDotTorrentsCommand.NotifyCanExecuteChanged();
            AddLabelCommand.NotifyCanExecuteChanged();

            SelectionDisposables.Add(this.CurrentlySelectedItems.Connect().WhenPropertyChanged(x => x.Labels, notifyOnInitialValue: true).Subscribe(x => {
                LabelsWithAdd = GetLabelsWithAdd(TorrentPolling.AllLabelReferences.Keys.ToArray());
            }));
        }
    }

    public static class ExampleTorrentListingViewModel
    {
        public static TorrentListingViewModel ViewModel { get; }
    }
}
