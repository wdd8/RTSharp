using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridConditionalFormatting;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Media;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NP.UniDockService;
using NP.Utilities;

using RTSharp.Core;
using RTSharp.Core.TorrentPolling;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Controls.DataGridFilters;
using RTSharp.Shared.Utils;
using RTSharp.ViewModels.Options;
using RTSharp.Views.Options;
using RTSharp.Views.TorrentListing;

using Serilog;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

using Torrent = RTSharp.Models.Torrent;

namespace RTSharp.ViewModels.TorrentListing;

public class DockTorrentListingViewModel : DockItemViewModel<TorrentListingViewModel> { }

public readonly struct TrackerFilterContext : IComparable
{
    public required string? Name { get; init; }

    public required IImage? Icon { get; init; }

    public int CompareTo(object? obj)
    {
        if (obj is not TrackerFilterContext b)
            return 1;

        return String.Compare(Name, b.Name, StringComparison.Ordinal);
    }

    public override bool Equals(object obj) => obj is TrackerFilterContext b && Name == b.Name;
    public override int GetHashCode() => String.GetHashCode(Name);
}

public partial class TorrentListingViewModel : ObservableObject, IContextPopulatedNotifyable, IDockable
{
    public ObservableRangeCollection<Torrent> Torrents { get; } = TorrentPolling.Torrents;

    public DataGridCollectionView View { get; }
    public SearchModel SearchModel { get; } = new SearchModel {
        HighlightMode = SearchHighlightMode.None,
        HighlightCurrent = false,
        WrapNavigation = false,
        UpdateSelectionOnNavigate = false
    };
    public SortingModel SortingModel { get; } = new SortingModel {
        MultiSort = true,
        CycleMode = SortCycleMode.AscendingDescendingNone,
        OwnsViewSorts = true
    };
    public SelectionModel<Torrent> SelectionModel { get; } = new SelectionModel<Torrent> {
        SingleSelect = false
    };
    public FilteringModel FilteringModel { get; } = new() {
        OwnsViewFilter = true
    };
    public ObservableCollection<DataGridColumnDefinition> ColumnDefinitions { get; }

    public ObservableCollection<Torrent> SelectedItems { get; } = new();

    public SetFilterContext<string> ConnectionFilter { get; }
    public TextFilterContext HashFilter { get; }
    public TextFilterContext NameFilter { get; }
    public SetFilterContext<string> StateFilter { get; }
    public SetFilterContext<string> LabelsFilter { get; }
    public SetFilterContext<TrackerFilterContext> TrackerFilter { get; }

    public DataGridColumnDefinition NameColumn, LabelsColumn, TrackerColumn;

    public DateTime LastSearchKey { get; set; }
    public TimeSpan SearchAsYouGoDelay { get; private set; }
    public string CurrentSearchText { get; set; }

    [ObservableProperty]
    public partial TemplatedControl[] LabelsWithAdd { get; set; }
    public GeneralTorrentInfoViewModel GeneralInfoViewModel { get; } = new();

    public TorrentFilesViewModel FilesViewModel { get; } = new();

    public TorrentPeersViewModel PeersViewModel { get; } = new();

    public TorrentTrackersViewModel TrackersViewModel { get; }

    private static readonly DrawingImage DefaultImage;

    private bool StartTorrentAllowed => SelectedItems.Any() && SelectedItems[0].InternalState != TORRENT_STATE.SEEDING;

    public string HeaderName => "Torrent listing";

    public Geometry? Icon => null;

    public Func<string?> CaptureGridState;
    public Action<object> ScrollToItem;

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

    public TorrentListingViewModel()
    {
        this.TrackersViewModel = new(this);

        using (var scope = Core.ServiceProvider.CreateScope()) {
            var config = scope.ServiceProvider.GetRequiredService<IOptions<Config.Models.Behavior>>();
            SearchAsYouGoDelay = config.Value.SearchAsYouGoDelay;
        }

        View = new DataGridCollectionView(Torrents);
        var builder = DataGridColumnDefinitionBuilder.For<Torrent>();
        DataGridColumnDefinition connectionColumn, hashColumn, stateColumn, priorityColumn;

        ColumnDefinitions =
        [
            (connectionColumn = builder.Text(
                header: "Connection",
                property: Utils.CreateProperty<string>("Connection", x => ((Torrent)x).DataOwner.PluginInstance.PluginInstanceConfig.Name, null),
                getter: x => x.DataOwner.PluginInstance.PluginInstanceConfig.Name,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(50);
                    c.ColumnKey = "Connection";
                    c.FilterFlyoutKey = "ConnectionFilterFlyout";
                }
            )),
            (hashColumn = builder.Template(
                header: nameof(Torrent.Hash),
                cellTemplateKey: "TorrentHashTemplate",
                configure: c => {
                    c.Width = new DataGridLength(16);
                    c.ColumnKey = nameof(Torrent.Hash);
                    c.ValueAccessor = new DataGridColumnValueAccessor<Torrent, byte[]>(x => x.Hash);
                    c.FilterFlyoutKey = "HashFilterFlyout";
                    c.ReuseCellContent = true;
                }
            )),
            (NameColumn = builder.Text(
                header: nameof(Torrent.Name),
                property: Utils.CreateProperty<string>(nameof(Torrent.Name), x => ((Torrent)x).Name, null),
                getter: x => x.Name,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(600);
                    c.ColumnKey = nameof(Torrent.Name);
                    c.FilterFlyoutKey = "NameFilterFlyout";
                }
            )),
            (stateColumn = builder.Text(
                header: "State",
                property: Utils.CreateProperty<string>(nameof(Torrent.State), x => ((Torrent)x).State, null),
                getter: x => x.State,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(60);
                    c.ColumnKey = nameof(Torrent.State);
                    c.FilterFlyoutKey = "StateFilterFlyout";
                }
            )),
            builder.Text(
                header: "Size",
                property: Utils.CreateProperty<string>(nameof(Torrent.SizeDisplay), x => ((Torrent)x).SizeDisplay, null),
                getter: x => x.Size,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(70);
                    c.ColumnKey = nameof(Torrent.Size);
                }
            ),
            builder.Template(
                header: "Done",
                cellTemplateKey: "TorrentDoneTemplate",
                configure: c => {
                    c.Width = new DataGridLength(70);
                    c.ColumnKey = nameof(Torrent.Done);
                    c.ValueAccessor = new DataGridColumnValueAccessor<Torrent, float>(x => x.Done);
                    c.ReuseCellContent = true;
                }
            ),
            builder.Text(
                header: "Downloaded",
                property: Utils.CreateProperty<string>(nameof(Torrent.DownloadedDisplay), x => ((Torrent)x).DownloadedDisplay, null),
                getter: x => x.Downloaded,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(70);
                    c.ColumnKey = nameof(Torrent.Downloaded);
                }
            ),
            builder.Text(
                header: "Completed",
                property: Utils.CreateProperty<string>(nameof(Torrent.CompletedSizeDisplay), x => ((Torrent)x).CompletedSizeDisplay, null),
                getter: x => x.CompletedSize,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(70);
                    c.ColumnKey = nameof(Torrent.CompletedSize);
                }
            ),
            builder.Text(
                header: "Uploaded",
                property: Utils.CreateProperty<string>(nameof(Torrent.UploadedDisplay), x => ((Torrent)x).UploadedDisplay, null),
                getter: x => x.Uploaded,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(70);
                    c.ColumnKey = nameof(Torrent.Uploaded);
                }
            ),
            builder.Text(
                header: "Remaining",
                property: Utils.CreateProperty<string>(nameof(Torrent.RemainingSizeDisplay), x => ((Torrent)x).RemainingSizeDisplay, null),
                getter: x => x.RemainingSize,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(70);
                    c.ColumnKey = nameof(Torrent.RemainingSize);
                }
            ),
            builder.Text(
                header: "DL Speed",
                property: Utils.CreateProperty<string>(nameof(Torrent.DLSpeedDisplay), x => ((Torrent)x).DLSpeedDisplay, null),
                getter: x => x.DLSpeed,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(70);
                    c.ColumnKey = nameof(Torrent.DLSpeed);
                }
            ),
            builder.Text(
                header: "UP Speed",
                property: Utils.CreateProperty<string>(nameof(Torrent.UPSpeedDisplay), x => ((Torrent)x).UPSpeedDisplay, null),
                getter: x => x.UPSpeed,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(70);
                    c.ColumnKey = nameof(Torrent.UPSpeed);
                }
            ),
            builder.Text(
                header: "Peers",
                property: Utils.CreateProperty<string>(nameof(Torrent.PeersDisplay), x => ((Torrent)x).PeersDisplay, null),
                getter: x => x.Peers.Connected,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(43);
                    c.ColumnKey = nameof(Torrent.Peers);
                }
            ),
            builder.Text(
                header: "Seeders",
                property: Utils.CreateProperty<string>(nameof(Torrent.SeedersDisplay), x => ((Torrent)x).SeedersDisplay, null),
                getter: x => x.Seeders.Connected,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(43);
                    c.ColumnKey = nameof(Torrent.Seeders);
                }
            ),
            (LabelsColumn = builder.Text(
                header: "Labels",
                property: Utils.CreateProperty<string>(nameof(Torrent.LabelsDisplay), x => ((Torrent)x).LabelsDisplay, null),
                getter: x => x.LabelsDisplay,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(70);
                    c.ColumnKey = nameof(Torrent.Labels);
                    c.FilterFlyoutKey = "LabelsFilterFlyout";
                }
            )),
            builder.Text(
                header: "ETA",
                property: Utils.CreateProperty<string>(nameof(Torrent.ETADisplay), x => ((Torrent)x).ETADisplay, null),
                getter: x => x.ETA,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(70);
                    c.ColumnKey = nameof(Torrent.ETA);
                }
            ),
            builder.Text(
                header: "Created On",
                property: Utils.CreateProperty<string>(nameof(Torrent.CreatedOnDateDisplay), x => ((Torrent)x).CreatedOnDateDisplay, null),
                getter: x => x.CreatedOnDate,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(120);
                    c.ColumnKey = nameof(Torrent.CreatedOnDate);
                }
            ),
            builder.Text(
                header: "Added On",
                property: Utils.CreateProperty<string>(nameof(Torrent.AddedOnDateDisplay), x => ((Torrent)x).AddedOnDateDisplay, null),
                getter: x => x.AddedOnDate,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(120);
                    c.ColumnKey = nameof(Torrent.AddedOnDate);
                }
            ),
            builder.Text(
                header: "Ratio",
                property: Utils.CreateProperty<string>(nameof(Torrent.RatioDisplay), x => ((Torrent)x).RatioDisplay, null),
                getter: x => x.Ratio,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(70);
                    c.ColumnKey = nameof(Torrent.Ratio);
                }
            ),
            builder.Text(
                header: "Finished On",
                property: Utils.CreateProperty<string>(nameof(Torrent.FinishedOnDateDisplay), x => ((Torrent)x).FinishedOnDateDisplay, null),
                getter: x => x.FinishedOnDate,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(120);
                    c.ColumnKey = nameof(Torrent.FinishedOnDate);
                }
            ),
            (TrackerColumn = builder.Template(
                header: "Tracker",
                cellTemplateKey: "TorrentTrackerTemplate",
                configure: c => {
                    c.Width = new DataGridLength(120);
                    c.ColumnKey = nameof(Torrent.TrackerDisplayName);
                    c.ValueAccessor = new DataGridColumnValueAccessor<Torrent, string?>(x => x.TrackerDisplayName);
                    c.FilterFlyoutKey = "TrackerFilterFlyout";
                    c.ReuseCellContent = true;
                }
            )),
           (priorityColumn = builder.Text(
                header: "Priority",
                property: Utils.CreateProperty<string>(nameof(Torrent.Priority), x => ((Torrent)x).Priority, null),
                getter: x => x.Priority,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(70);
                    c.ColumnKey = nameof(Torrent.Priority);
                    c.FilterFlyoutKey = "PriorityFilterFlyout";
                }
            ))
        ];

        Dispatcher.UIThread.RunJobs();

        var model = new ConditionalFormattingModel();
        model.Apply(new[]
        {
            new ConditionalFormattingDescriptor(
                ruleId: "row-alert",
                @operator: ConditionalFormattingOperator.Custom,
                columnId: nameof(Torrent.Ratio),
                predicate: x => ((Torrent)x.Item).Ratio > 5,
                target: ConditionalFormattingTarget.Cell,
                valueSource: ConditionalFormattingValueSource.Item,
                themeKey: "RowAlertTheme")
        });

        ConnectionFilter = new SetFilterContext<string>("Connection equals", FilteringModel, connectionColumn, FilteringOperator.In);

        HashFilter = new TextFilterContext("Hash is", FilteringModel, hashColumn, FilteringOperator.Custom, (x, opt) => {
            if (opt == null || !ConvertExtensions.TryFromHexString(opt, out var res))
                return false;
            return res.SequenceEqual(((Torrent)x!).Hash);
        });
        NameFilter = new TextFilterContext("Name contains", FilteringModel, NameColumn, FilteringOperator.Contains);
        StateFilter = new SetFilterContext<string>("State equals", FilteringModel, stateColumn, FilteringOperator.Custom, (x, opts) => opts.Any(i => ((Torrent)x!).InternalState.HasFlag(EnumExt.ToTorrentState((string)i!))));
        LabelsFilter = new SetFilterContext<string>("Label equals", FilteringModel, LabelsColumn, FilteringOperator.In);
        TrackerFilter = new SetFilterContext<TrackerFilterContext>("Tracker equals", FilteringModel, TrackerColumn, FilteringOperator.Custom, (x, opts) => opts.Any(i => ((TrackerFilterContext)i!).Name == ((Torrent)x!).TrackerDisplayName));

        SelectionModel.SelectionChanged += CurrentlySelectedTorrentsChanged;

        App.RegisterOnExit($"{nameof(TorrentListingView)}_{nameof(DataGrid)}_{nameof(SaveGridState)}", SaveGridState);
    }

    ~TorrentListingViewModel() // TODO: this doesn't work
    {
        foreach (var item in VMDisposables)
            item.Dispose();

        VMDisposables.Clear();
    }

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

        if (SelectedItems == null)
            return Array.Empty<TemplatedControl>();

        var current = SelectedItems.ToArray();

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
            FilesViewModel.ClearRoot();
            PeersViewModel.Peers.Clear();
            TrackersViewModel.Trackers.Clear();
            return;
        }

        var newTorrent = NewTorrents[0];

        GeneralInfoViewModel.Torrent = newTorrent;
        FilesViewModel.Torrent = newTorrent;
        PeersViewModel.Torrent = newTorrent;
        TrackersViewModel.Torrent = newTorrent;
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

    public void OnContextPopulated()
    {
        VMDisposables.Add(TorrentPolling.AllLabelReferencesObservable.Subscribe(x => LabelsWithAdd = GetLabelsWithAdd(x)));
    }

    private void CurrentlySelectedTorrentsChanged(object? sender, SelectionModelSelectionChangedEventArgs<Torrent> e)
    {
        foreach (var item in SelectionDisposables)
            item.Dispose();

        SelectionDisposables.Clear();

#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
        IReadOnlyList<Torrent> selection = e.SelectedItems ?? [];
#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.

        UpdateTorrentInChildViewModels(selection);
        _ = PollTorrentInfo(selection);
        this.OnPropertyChanged(nameof(StartTorrentAllowed));
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

        void TorrentPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Torrent.Labels)) {
                LabelsWithAdd = GetLabelsWithAdd(TorrentPolling.AllLabelReferences.Keys.ToArray());
            }
        }

        foreach (var item in selection) {
            item.PropertyChanged += TorrentPropertyChanged;
            SelectionDisposables.Add(Disposable.Create(() => {
                item.PropertyChanged -= TorrentPropertyChanged;
            }));
        }
    }
}

public static class ExampleTorrentListingViewModel
{
    public static TorrentListingViewModel ViewModel { get; }
}
