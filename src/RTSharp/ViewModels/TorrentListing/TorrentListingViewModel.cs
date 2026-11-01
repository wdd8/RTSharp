using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSelection;
using Avalonia.Controls.DataGridSorting;
using Avalonia.Controls.Primitives;
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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive.Disposables;
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

public partial class TorrentListingViewModel : ObservableViewModel
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
    public DataGridSelectionModel<Torrent> SelectionModel { get; } = new DataGridSelectionModel<Torrent> {
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

    public ObservableCollection<TemplatedControl> LabelsWithAdd { get; } = new();
    public GeneralTorrentInfoViewModel GeneralInfoViewModel { get; } = new();

    public TorrentFilesViewModel FilesViewModel { get; } = new();

    public TorrentPeersViewModel PeersViewModel { get; } = new();

    public TorrentTrackersViewModel TrackersViewModel { get; }

    [ObservableProperty]
    public partial int SelectedTorrentDetailsTabIndex { get; set; }

    private static readonly DrawingImage DefaultImage;

    private bool StartTorrentAllowed => SelectedItems.Any() && SelectedItems[0].InternalState != TORRENT_STATE.SEEDING;

    public string HeaderName => "Torrent listing";

    public Geometry? Icon => null;

    public Func<string?>? CaptureGridState; // view set
    public Func<double[]?>? CaptureGridRowHeights; // view set
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

    private bool ContextPopulated;

    private readonly Dictionary<string, (MenuItem Container, CheckBox Icon)> LabelControlCache = new();
    private readonly Separator LabelSeparator = new();
    private readonly MenuItem AddLabelMenuItem;

    public TorrentListingViewModel()
    {
        this.TrackersViewModel = new(this);

        using (var scope = Core.ServiceProvider.CreateScope()) {
            var config = scope.ServiceProvider.GetRequiredService<IOptions<Config.Models.Behavior>>();
            SearchClearTimer = new DispatcherTimer(config.Value.SearchAsYouGoDelay, DispatcherPriority.Normal, (_, _) => {
                CurrentSearchText = "";
                SearchClearTimer!.Stop();
            });
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
                property: Utils.CreateProperty<string>("Connection", x => ((Torrent)x).DataOwner.Config.Plugin.Name, null),
                getter: x => x.DataOwner.Config.Plugin.Name,
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
                header: "Hashing Speed",
                property: Utils.CreateProperty<string>(nameof(Torrent.HashingSpeedDisplay), x => ((Torrent)x).HashingSpeedDisplay, null),
                getter: x => x.HashingSpeed,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(70);
                    c.ColumnKey = nameof(Torrent.HashingSpeed);
                    c.CustomSortComparer = Comparer<ulong>.Default;
                }
            ),
            builder.Text(
                header: "DL + Hashing Speed",
                property: Utils.CreateProperty<string>(nameof(Torrent.DLPlusHashingSpeedDisplay), x => ((Torrent)x).DLPlusHashingSpeedDisplay, null),
                getter: x => x.DLSpeed + x.HashingSpeed,
                setter: null,
                configure: c =>
                {
                    c.Width = new DataGridLength(70);
                    c.ColumnKey = nameof(Torrent.DLPlusHashingSpeedDisplay);
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

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Torrent))]
    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Accesses Torrent")]
    public void AttachGridData()
    {
        View = new DataGridCollectionView(VisibleTorrents);
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
            DataContext = new OptionsWindowViewModel()
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

    [ObservableProperty]
    public partial string SequentialDownloadMenuHeader { get; set; } = "Download sequentially";

    public void RefreshSequentialDownloadMenuHeader()
    {
        SequentialDownloadMenuHeader = SelectedItems.Count > 0 && SelectedItems.All(x => x.IsSequentialDownload)
            ? "Do not download sequentially"
            : "Download sequentially";
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

    private void ClearSummary()
    {
        GeneralInfoViewModel.Summary = null;
    }

    private void UpdateSelectionSummary(IReadOnlyList<Torrent>? NewTorrents)
    {
        if (NewTorrents == null || NewTorrents.Count < 2) {
            ClearSummary();
            return;
        }

        GeneralInfoViewModel.Summary ??= new TorrentSelectionSummary();
        GeneralInfoViewModel.Summary.Update(NewTorrents);
    }

    private void TorrentPolling_TorrentBatchChange(object? sender, TorrentStoreChangeSet e)
    {
        UpdateSelectionSummary(SelectionModel.SelectedItems);
    }

    private void UpdateTorrentInChildViewModels(IReadOnlyList<Torrent>? Selection)
    {
        UpdateSelectionSummary(Selection);

        if (Selection?.Count != 1) {
            GeneralInfoViewModel.Torrent = null;
            FilesViewModel.Torrent = null;
            FilesViewModel.ClearRoot();
            PeersViewModel.Peers.Clear();
            TrackersViewModel.Trackers.Clear();
            return;
        }

        var newTorrent = Selection[0];

        if (!IsCurrentTabTarget(newTorrent)) {
            FilesViewModel.ClearRoot();
            PeersViewModel.Peers.Clear();
            TrackersViewModel.Trackers.Clear();

            // Others are fine to clear but pieces progress bar is really visible in general view when it gets cleared
            IList<PieceState> pieces = [];
            if (newTorrent.DataOwner.Instance.Capabilities.GetPieces) {
                if (newTorrent.IsCompleted)
                    pieces = [ PieceState.Downloaded ];
                else if (newTorrent.WantedSize == 0)
                    pieces = [ PieceState.Unknown ];
                else if (newTorrent.CompletedSize == 0)
                    pieces = [ PieceState.NotDownloaded ];
            }
            GeneralInfoViewModel.Pieces = pieces;
        }

        GeneralInfoViewModel.Torrent = newTorrent;
        FilesViewModel.Torrent = newTorrent;
        PeersViewModel.Torrent = newTorrent;
        TrackersViewModel.Torrent = newTorrent;
    }

    private bool IsCurrentTabTarget(Torrent Torrent) => TorrentTabsTarget != null && Torrent.DataOwner == TorrentTabsTarget.DataOwner && Torrent.Hash.SequenceEqual(TorrentTabsTarget.Hash);

    private enum TORRENT_DETAILS_TAB
    {
        GENERAL = 0,
        FILES = 1,
        PEERS = 2,
        TRACKERS = 3
    }

    private static readonly TimeSpan SelectionDebounceInterval = TimeSpan.FromMilliseconds(120);

    private Task? TorrentTabsTasks;
    private CancellationTokenSource? SelectionChange { get; set; }
    private Torrent? TorrentTabsTarget;
    private TORRENT_DETAILS_TAB? TorrentDetailsTabTaskTarget;

    private void PollTorrentInfo(IReadOnlyList<Torrent> Selection, bool ForceRestart = false)
    {
        var selection = Selection.Count != 1 ? null : Selection[0];

        if (!ForceRestart && selection != null && IsCurrentTabTarget(selection)) {
            return;
        }

        if (SelectionChange != null) {
            SelectionChange.Cancel();

            var oldTasks = TorrentTabsTasks;
            _ = Task.Run(async () => {
                try {
                    if (oldTasks != null)
                        await oldTasks;
                } catch (OperationCanceledException) {
                } catch (Exception ex) {
                    Log.Logger.Error(ex, "One of torrent tasks failed");
                }
            });
        }

        SelectionChange = null;
        TorrentTabsTasks = null;
        TorrentTabsTarget = selection;

        if (selection == null) {
            TorrentDetailsTabTaskTarget = null;
            return;
        }

        SelectionChange = new CancellationTokenSource();
        TorrentTabsTasks = StartTorrentTabTasks(selection, SelectionChange.Token);
    }

    private static async Task WaitForCancellation(CancellationToken Token)
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (Token.UnsafeRegister(static state => ((TaskCompletionSource)state!).SetResult(), cancelled))
            await cancelled.Task;
    }

    private async Task StartTorrentTabTasks(Torrent NewTorrent, CancellationToken SelectionChange)
    {
        await Task.WhenAny(Task.Delay(SelectionDebounceInterval, SelectionChange));

        if (SelectionChange.IsCancellationRequested)
            return;

        Log.Logger.Debug("Starting tasks for: " + NewTorrent.Name);

        bool pollActiveTabOnly;
        using (var scope = Core.ServiceProvider.CreateScope())
            pollActiveTabOnly = scope.ServiceProvider.GetRequiredService<Config>().Behavior.Value.PollActiveTabOnly;

        if (!pollActiveTabOnly) {
            TorrentDetailsTabTaskTarget = null;
            await Task.WhenAll(
                FilesTasks(NewTorrent, SelectionChange),
                PeersTasks(NewTorrent, SelectionChange),
                TrackersTasks(NewTorrent, SelectionChange)
            );
            return;
        }

        var group = (TORRENT_DETAILS_TAB)SelectedTorrentDetailsTabIndex;
        TorrentDetailsTabTaskTarget = group;

        await (group switch {
            TORRENT_DETAILS_TAB.GENERAL => throw new InvalidOperationException(),
            TORRENT_DETAILS_TAB.FILES => FilesTasks(NewTorrent, SelectionChange),
            TORRENT_DETAILS_TAB.PEERS => PeersTasks(NewTorrent, SelectionChange),
            TORRENT_DETAILS_TAB.TRACKERS => TrackersTasks(NewTorrent, SelectionChange),
            _ => throw new ArgumentOutOfRangeException()
        });
    }

    partial void OnSelectedTorrentDetailsTabIndexChanged(int value)
    {
        bool pollActiveTabOnly;
        using (var scope = Core.ServiceProvider.CreateScope())
            pollActiveTabOnly = scope.ServiceProvider.GetRequiredService<Config>().Behavior.Value.PollActiveTabOnly;

        if (!pollActiveTabOnly)
            return;

        if (TorrentDetailsTabTaskTarget == (TORRENT_DETAILS_TAB)value)
            return;

        var selection = SelectedItems.ToArray();
        if (selection.Length != 1)
            return;

        PollTorrentInfo(selection, ForceRestart: true);
    }

    public override void OnContextPopulated()
    {
        if (ContextPopulated)
            return;

        ContextPopulated = true;
        AddDisposable(TorrentPolling.AllLabelReferencesObservable.Subscribe(UpdateLabelsWithAdd));

        TorrentPolling.Torrents.Changed += TorrentPolling_TorrentBatchChange;
        AddDisposable(Disposable.Create(() => TorrentPolling.Torrents.Changed -= TorrentPolling_TorrentBatchChange));
    }

    private void CurrentlySelectedTorrentsChanged(object? sender, DataGridSelectionModelChangedEventArgs<Torrent> e)
    {
        var selection = SelectionModel.SelectedItems;
        UpdateTorrentInChildViewModels(selection);
        PollTorrentInfo(selection);

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
            EditTorrentCommand.NotifyCanExecuteChanged();
            AddLabelCommand.NotifyCanExecuteChanged();
        });

    }
}

public static class ExampleTorrentListingViewModel
{
    public static TorrentListingViewModel ViewModel { get; } = new();
}
