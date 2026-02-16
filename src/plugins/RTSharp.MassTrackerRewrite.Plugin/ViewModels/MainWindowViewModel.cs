using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MsBox.Avalonia;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Controls;

using System.Collections.ObjectModel;

namespace RTSharp.MassTrackerRewrite.Plugin.ViewModels;

public partial class MainWindowViewModel(IPluginHost Host, MainWindow Window) : ObservableObject, IContextPopulatedNotifyable
{
    public record TrackerInfo(string Uri, int Count)
    {
        public override string ToString() => Uri + " (" + Count + ")";
    }

    public ObservableCollection<TrackerInfo> Trackers { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoCommand))]
    private TrackerInfo? selectedTracker;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoCommand))]
    private string urlText = string.Empty;

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private double progressValue = 0;

    [ObservableProperty]
    private bool isBusy = false;

    [ObservableProperty]
    private bool progressDialogShown = true;

    public bool CanExecuteGo()
    {
        return !IsBusy && SelectedTracker != null && !string.IsNullOrWhiteSpace(UrlText);
    }
    [RelayCommand(CanExecute = nameof(CanExecuteGo))]
    public async Task Go()
    {
        IsBusy = true;
        GoCommand.NotifyCanExecuteChanged();
        StatusText = "Fetching torrents...";
        ProgressValue = 0;

        var affectedCountExpected = SelectedTracker.Count;

        var affectedTorrents = new List<Torrent>();

        try {
            foreach (var group in Host.Torrents.GroupBy(x => x.DataOwner)) {
                var torrents = await group.Key.GetTrackers([.. group], default /* TODO: bind to window closing!! */);
                foreach (var torrent in torrents) {
                    foreach (var tracker in torrent.Value) {
                        if (tracker.Uri == SelectedTracker!.Uri) {
                            affectedTorrents.Add(group.First(x => x.Hash.SequenceEqual(torrent.Key)));
                        }
                    }
                }
            }
        } catch (Exception ex) {
            Host.Logger.Error(ex, "Failed to get trackers from torrents");
            IsBusy = false;
            StatusText = "Failed";
            ProgressValue = 0;
        }

        if (affectedTorrents.Count != affectedCountExpected) {
            var msg = string.Format("Expected {0} affected torrents, but found {1}", affectedCountExpected, affectedTorrents.Count);
            Host.Logger.Warning(msg);
            StatusText = msg;
            IsBusy = false;
            ProgressValue = 0;
            return;
        }

        StatusText = affectedTorrents.Count + " affected torrents...";

        for (var x = 0;x < affectedTorrents.Count;x++) {
            var torrent = affectedTorrents[x];
            var trackers = await torrent.DataOwner.GetTrackers([ torrent ]);
            try {
                StatusText = torrent.Name + "...";

                await torrent.DataOwner.Tracker.ReplaceTracker(torrent, SelectedTracker.Uri, UrlText);
            } catch (Exception ex) {
                Host.Logger.Error(ex, "Failed to replace tracker for torrent " + torrent.Name);
                StatusText = "Failed to replace tracker for torrent " + torrent.Name;
            }

            ProgressValue = (double)x / affectedTorrents.Count * 100;
        }

        StatusText = "Done";
        ProgressValue = 0;
        IsBusy = false;
        GoCommand.NotifyCanExecuteChanged();

        OnContextPopulated();
    }

    public void OnContextPopulated()
    {
        _ = Task.Run(async () => {
            var trackers = new Dictionary<string, int>();

            bool hasRtorrent = false;

            try {
                foreach (var group in Host.Torrents.GroupBy(x => x.DataOwner)) {
                    if (!group.Key.Tracker.Capabilities.ReplaceTracker)
                        continue;

                    if (group.Key.GUID == new Guid("90F180F2-F1D3-4CAA-859F-06D80B5DCF5C")) {
                        hasRtorrent = true;
                    }

                    var torrents = await group.Key.GetTrackers([.. group], default /* TODO: bind to window closing!! */);
                    foreach (var torrent in torrents) {
                        foreach (var tracker in torrent.Value) {
                            if (!trackers.TryGetValue(tracker.Uri, out var count)) {
                                trackers[tracker.Uri] = 1;
                            } else {
                                trackers[tracker.Uri] = count + 1;
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                Host.Logger.Error(ex, "Failed to get trackers from torrents");

                Dispatcher.UIThread.Invoke(() => {
                    ProgressDialogShown = false;
                });

                return;
            }

            Dispatcher.UIThread.Invoke(() => {
                if (hasRtorrent) {
                    _ = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams() {
                        ButtonDefinitions = MsBox.Avalonia.Enums.ButtonEnum.Ok,
                        ContentTitle = "RT# - Mass Tracker Rewrite",
                        ContentMessage = "rtorrent does not support natively replacing trackers in-place, so a workaround is applied that basically removes, modifies and readds the torrent. This has undesired side-effects like losing historical DL/UL data on the torrent.\n\nIt is recommended to take rtorrent offline, replace references to the tracker URL in rtorrent's session folder, and start it up again. This tool will use the workaround if needed and will require a full torrent rehash after.",
                        Icon = MsBox.Avalonia.Enums.Icon.Warning
                    }).ShowWindowDialogAsync(Window);
                }

                Trackers.Clear();

                foreach (var (tracker, count) in trackers.OrderBy(x => x.Key)) {
                    Trackers.Add(new(tracker, count));
                }

                ProgressDialogShown = false;
            });
        });
    }
}
