using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using RTSharp.Shared.Abstractions;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.ViewModels.Statistics
{
    public partial class StatisticsWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        public partial int TotalTorrents { get; set; }

        [ObservableProperty]
        public partial ulong TotalSeedingSize { get; set; }

        [ObservableProperty]
        public partial ulong TotalUploaded { get; set; }

        [ObservableProperty]
        public partial ulong TotalDownloaded { get; set; }

        [ObservableProperty]
        public partial float ShareRatio { get; set; }

        [ObservableProperty]
        public partial ulong AllTimeUpload { get; set; }

        [ObservableProperty]
        public partial ulong AllTimeDownload { get; set; }

        [ObservableProperty]
        public partial float AllTimeShareRatio { get; set; }

        [ObservableProperty]
        public partial bool SomeDontSupport { get; set; }
        public PeriodicTimer Timer { get; set; }

        public void RunTimer()
        {
            Timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

            _ = Task.Run(async () => {
                while (await Timer.WaitForNextTickAsync()) {
                    await RefreshStats();
                }
            });
        }

        public async Task RefreshStats()
        {
            IReadOnlyCollection<Models.Torrent> snap = [.. Core.TorrentPolling.TorrentPolling.Torrents];

            var total = 0;
            ulong totalSeedingSize = 0, totalUploaded = 0, totalDownloaded = 0;

            var perOwnerSnapshotTotals = new Dictionary<object, (ulong Uploaded, ulong Downloaded)>();

            foreach (var torrent in snap) {
                total++;

                if (torrent.InternalState.HasFlag(Shared.Abstractions.TORRENT_STATE.SEEDING))
                    totalSeedingSize += torrent.WantedSize;

                totalUploaded += torrent.Uploaded;
                totalDownloaded += torrent.Downloaded;

                var ownerKey = torrent.DataOwner!;
                if (perOwnerSnapshotTotals.TryGetValue(ownerKey, out var cur)) {
                    perOwnerSnapshotTotals[ownerKey] = (cur.Uploaded + torrent.Uploaded, cur.Downloaded + torrent.Downloaded);
                } else {
                    perOwnerSnapshotTotals[ownerKey] = (torrent.Uploaded, torrent.Downloaded);
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() => {
                TotalTorrents = total;
                TotalSeedingSize = totalSeedingSize;
                TotalUploaded = totalUploaded;
                TotalDownloaded = totalDownloaded;
                ShareRatio = totalDownloaded == 0 ? 0f : (float)totalUploaded / totalDownloaded;
            });

            var owners = snap.GroupBy(x => x.DataOwner).ToArray();

            if (owners.Length != owners.Count(x => x.Key.Instance.Stats.Capabilities.GetAllTimeDataStats) && !SomeDontSupport) {
                await Dispatcher.UIThread.InvokeAsync(() => {
                    SomeDontSupport = true;
                });
            }

            var allTimeStats = await Task.WhenAll(owners.Select(async x => {
                if (!x.Key.Instance.Stats.Capabilities.GetAllTimeDataStats) {
                    ulong dl = 0, up = 0;
                    foreach (var torrent in x) {
                        dl += torrent.Downloaded;
                        up += torrent.Uploaded;
                    }

                    return new AllTimeDataStats(dl, up, 0);
                }
                return await x.Key.Instance.Stats.GetAllTimeDataStats(default);
            }));

            await Dispatcher.UIThread.InvokeAsync(() => {
                AllTimeUpload = 0;
                AllTimeDownload = 0;
                AllTimeShareRatio = 0;
                foreach (var stats in allTimeStats) {
                    AllTimeUpload += stats.BytesUploaded;
                    AllTimeDownload += stats.BytesDownloaded;
                }
                AllTimeShareRatio = AllTimeDownload == 0 ? 0f : (float)AllTimeUpload / AllTimeDownload;
            });
        }
    }
}
