using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using RTSharp.Shared.Abstractions;

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.ViewModels.Statistics
{
    public partial class StatisticsWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        public int totalTorrents;

        [ObservableProperty]
        public ulong totalSeedingSize;

        [ObservableProperty]
        public ulong totalUploaded;

        [ObservableProperty]
        public ulong totalDownloaded;

        [ObservableProperty]
        public float shareRatio;

        [ObservableProperty]
        public ulong allTimeUpload;

        [ObservableProperty]
        public ulong allTimeDownload;

        [ObservableProperty]
        public float allTimeShareRatio;

        [ObservableProperty]
        public bool someDontSupport;

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
            var snap = Core.TorrentPolling.TorrentPolling.Torrents.Items.ToArray();

            var total = 0;
            ulong totalSeedingSize = 0, totalUploaded = 0, totalDownloaded = 0;

            // Per-owner snapshot totals: key is the owner object (same as grouping key)
            var perOwnerSnapshotTotals = new System.Collections.Generic.Dictionary<object, (ulong Uploaded, ulong Downloaded)>();

            foreach (var torrent in snap) {
                total++;

                if (torrent.InternalState.HasFlag(Shared.Abstractions.TORRENT_STATE.SEEDING))
                    totalSeedingSize += torrent.WantedSize;

                totalUploaded += torrent.Uploaded;
                totalDownloaded += torrent.Downloaded;

                var ownerKey = torrent.Owner!;
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

            // Log per-owner snapshot totals
            foreach (var kv in perOwnerSnapshotTotals) {
                var ownerKey = kv.Key;
                var up = kv.Value.Uploaded;
                var dl = kv.Value.Downloaded;
                try {
                    Debug.WriteLine($"[Stats][Snapshot][Owner:{ownerKey}] UP={up} DL={dl}");
                } catch {
                    // Swallow any logging exceptions to avoid breaking stats refresh
                }
            }

            var owners = snap.GroupBy(x => x.Owner).ToArray();

            if (owners.Count() != owners.Count(x => x.Key.Instance.Stats.Capabilities.GetAllTimeDataStats) && !SomeDontSupport) {
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

            // Log per-owner all-time totals; owners and allTimeStats preserve order from Select/WhenAll
            for (int i = 0; i < owners.Length && i < allTimeStats.Length; i++) {
                var owner = owners[i].Key;
                var stats = allTimeStats[i];
                try {
                    Debug.WriteLine($"[Stats][AllTime][Owner:{owner}] UP={stats.BytesUploaded} DL={stats.BytesDownloaded}");
                } catch {
                    // Ignore logging errors
                }
            }
        }
    }
}
