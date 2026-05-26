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

using RTSharp.Core;
using RTSharp.Core.TorrentPolling;
using RTSharp.Models;
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
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

using Torrent = RTSharp.Models.Torrent;

namespace RTSharp.ViewModels.TorrentListing;

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

    public override bool Equals(object? obj) => obj is TrackerFilterContext b && Name == b.Name;
    public override int GetHashCode() => String.GetHashCode(Name);
}

public partial class TorrentListingViewModel : ObservableObject, IContextPopulatedNotifyable
{
    public ReadOnlyObservableCollection<Torrent> VisibleTorrents;

    [ObservableProperty]
    public partial DataGridCollectionView View { get; set; } = null!; // set in AttachGridData

    public SearchModel SearchModel { get; } = new SearchModel {
        HighlightMode = SearchHighlightMode.None,
        HighlightCurrent = false,
        WrapNavigation = false,
        UpdateSelectionOnNavigate = false
    };
    public SortingModel SortingModel { get; } = new SortingModel {
        MultiSort = true,
        CycleMode = SortCycleMode.AscendingDescendingNone,
        OwnsViewSorts = true,
        KeepSecondarySorts = true
    };
    public SelectionModel<Torrent> SelectionModel { get; } = new SelectionModel<Torrent> {
        SingleSelect = false
    };
    public FilteringModel FilteringModel { get; } = new() {
        OwnsViewFilter = true
    };
    public TorrentSortingAdapterFactory SortingAdapterFactory { get; }
    public TorrentFilteringAdapterFactory FilteringAdapterFactory { get; }
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
    public string CurrentSearchText { get; set; } = "";

    public ObservableCollection<TemplatedControl> LabelsWithAdd { get; } = new();
    public GeneralTorrentInfoViewModel GeneralInfoViewModel { get; } = new();

    public TorrentFilesViewModel FilesViewModel { get; } = new();

    public TorrentPeersViewModel PeersViewModel { get; } = new();

    public TorrentTrackersViewModel TrackersViewModel { get; }

    private static readonly DrawingImage DefaultImage;

    private bool StartTorrentAllowed => SelectedItems.Any() && SelectedItems[0].InternalState != TORRENT_STATE.SEEDING;

    public string HeaderName => "Torrent listing";

    public Geometry? Icon => null;

    public Func<string?> CaptureGridState = null!; // view set
    public Action<object> ScrollToItem = null!; // view set

    static TorrentListingViewModel()
    {
        DefaultImage = new DrawingImage() {
            Drawing = new GeometryDrawing() {
                Geometry = FontAwesomeIcons.Get("fa7-solid fa7-globe"),
                Brush = Brushes.White
            }
        };
    }

    private List<IDisposable> VMDisposables = new();
    private bool contextPopulated;

    private readonly Dictionary<string, (MenuItem Container, CheckBox Icon)> LabelControlCache = new();
    private readonly Separator LabelSeparator = new();
    private readonly MenuItem AddLabelMenuItem;

    public TorrentListingViewModel()
    {
        this.TrackersViewModel = new(this);

        using (var scope = Core.ServiceProvider.CreateScope()) {
            var config = scope.ServiceProvider.GetRequiredService<IOptions<Config.Models.Behavior>>();
            SearchAsYouGoDelay = config.Value.SearchAsYouGoDelay;
        }

        VisibleTorrents = TorrentPolling.Torrents.VisibleItems;

        SortingAdapterFactory = new TorrentSortingAdapterFactory(TorrentPolling.Torrents.ApplySort);
        FilteringAdapterFactory = new TorrentFilteringAdapterFactory(TorrentPolling.Torrents.ApplyFilter);

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
                    c.CustomSortComparer = Comparer<byte[]>.Default;
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
                    c.CustomSortComparer = Comparer<string>.Default;
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
                    c.CustomSortComparer = Comparer<string>.Default;
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
                    c.CustomSortComparer = Comparer<ulong>.Default;
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
                    c.CustomSortComparer = Comparer<float>.Default;
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
                    c.CustomSortComparer = Comparer<ulong>.Default;
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
                    c.CustomSortComparer = Comparer<ulong>.Default;
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
                    c.CustomSortComparer = Comparer<ulong>.Default;
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
                    c.CustomSortComparer = Comparer<ulong>.Default;
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
                    c.CustomSortComparer = Comparer<ulong>.Default;
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
                    c.CustomSortComparer = Comparer<ulong>.Default;
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
                    c.CustomSortComparer = Comparer<uint>.Default;
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
                    c.CustomSortComparer = Comparer<uint>.Default;
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
                    c.ColumnKey = nameof(Torrent.LabelsDisplay);
                    c.FilterFlyoutKey = "LabelsFilterFlyout";
                    c.CustomSortComparer = Comparer<string>.Default;
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
                    c.CustomSortComparer = Comparer<TimeSpan>.Default;
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
                    c.CustomSortComparer = Comparer<DateTime?>.Default;
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
                    c.CustomSortComparer = Comparer<DateTime>.Default;
                    c.SortMemberPath = nameof(Torrent.AddedOnDate);
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
                    c.CustomSortComparer = Comparer<float>.Default;
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
                    c.CustomSortComparer = Comparer<DateTime?>.Default;
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

        ConnectionFilter = new SetFilterContext<string>("Connection equals", FilteringModel, connectionColumn, FilteringOperator.In);

        HashFilter = new TextFilterContext("Hash is", FilteringModel, hashColumn, FilteringOperator.Custom, (x, opt) => {
            if (opt == null || !ConvertExtensions.TryFromHexString(opt, out var res))
                return false;
            return res.SequenceEqual(((Torrent)x!).Hash);
        });
        NameFilter = new TextFilterContext("Name contains", FilteringModel, NameColumn, FilteringOperator.Contains);
        StateFilter = new SetFilterContext<string>("State equals", FilteringModel, stateColumn, FilteringOperator.Custom, (x, opts) => opts.Any(i => ((Torrent)x!).InternalState.HasFlag(EnumExt.ToTorrentState((string)i!))));
        LabelsFilter = new SetFilterContext<string>("Label equals", FilteringModel, LabelsColumn, FilteringOperator.Custom, (x, opts) => opts.Any(i => ((Torrent)x!).Labels.Contains((string)i!)));
        TrackerFilter = new SetFilterContext<TrackerFilterContext>("Tracker equals", FilteringModel, TrackerColumn, FilteringOperator.Custom, (x, opts) => opts.Any(i => ((TrackerFilterContext)i!).Name == ((Torrent)x!).TrackerDisplayName));

        SelectionModel.SelectionChanged += CurrentlySelectedTorrentsChanged;

        App.RegisterOnExit($"{nameof(TorrentListingView)}_{nameof(DataGrid)}_{nameof(SaveGridState)}", SaveGridState);

        AddLabelMenuItem = new MenuItem { Command = ShowAddLabelCommand, Header = "Add..." };
    }

    public void AttachGridData()
    {
        View = new DataGridCollectionView(VisibleTorrents);
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

    private void UpdateLabelsWithAdd(string[] allLabels)
    {
        LabelsWithAdd.Remove(AddLabelMenuItem);
        LabelsWithAdd.Remove(LabelSeparator);

        var newLabelSet = new HashSet<string>(allLabels);

        foreach (var stale in LabelControlCache.Keys.Where(k => !newLabelSet.Contains(k)).ToList()) {
            LabelsWithAdd.Remove(LabelControlCache[stale].Container);
            LabelControlCache.Remove(stale);
        }

        for (int i = 0; i < allLabels.Length; i++) {
            var label = allLabels[i];

            if (LabelControlCache.TryGetValue(label, out var controls)) {
                int curIdx = LabelsWithAdd.IndexOf(controls.Container);
                if (curIdx != i)
                    LabelsWithAdd.Move(curIdx, i);
            } else {
                var cb = new CheckBox {
                    BorderThickness = new Avalonia.Thickness(0),
                    IsHitTestVisible = true,
                    Command = ToggleLabelCommand,
                    IsThreeState = true,
                    IsChecked = false
                };
                var mi = new MenuItem { Icon = cb, Header = label };
                LabelControlCache[label] = (mi, cb);
                LabelsWithAdd.Insert(i, mi);
            }
        }

        if (allLabels.Length > 0)
            LabelsWithAdd.Add(LabelSeparator);
        LabelsWithAdd.Add(AddLabelMenuItem);
    }

    public void RefreshLabelCheckedState()
    {
        var current = SelectedItems.ToArray();
        foreach (var (label, (_, cb)) in LabelControlCache) {
            int checkedCount = 0;
            foreach (var torrent in current) {
                if (torrent.Labels.Contains(label))
                    checkedCount++;
            }
            cb.IsChecked = checkedCount == 0 ? false : (checkedCount == current.Length ? true : null);
            cb.CommandParameter = (current, label, checkedCount != 0);
        }
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

    private async Task PollTorrentInfo(IReadOnlyList<Torrent> NewTorrents)
    {
        var newTorrent = NewTorrents.Count != 1 ? null : NewTorrents[0];
        try {
            await TorrentTabsSwitch.WaitAsync();

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
        if (contextPopulated)
            return;

        contextPopulated = true;
        VMDisposables.Add(TorrentPolling.AllLabelReferencesObservable.Subscribe(x => UpdateLabelsWithAdd(x)));
    }

    private void CurrentlySelectedTorrentsChanged(object? sender, SelectionModelSelectionChangedEventArgs<Torrent> e)
    {
        var selection = (IReadOnlyList<Torrent>)e.SelectedItems;
        UpdateTorrentInChildViewModels(selection);
        _ = PollTorrentInfo(selection);

        this.OnPropertyChanged(nameof(StartTorrentAllowed));
        Dispatcher.UIThread.Post(() => {
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
        });

    }
}

public static class ExampleTorrentListingViewModel
{
    public static TorrentListingViewModel ViewModel { get; } = new();
}
