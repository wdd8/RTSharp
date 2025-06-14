using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using RTSharp.Shared.Controls;

using System;
using System.Collections.Frozen;
using System.Linq;

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
        public float shareRatio;

        private IDisposable Timer;

        public StatisticsWindowViewModel()
        {
            Timer = DispatcherTimer.Run(RefreshStats, TimeSpan.FromSeconds(1));
        }

        public bool RefreshStats()
        {
            var snap = Core.TorrentPolling.TorrentPolling.Torrents.Items.ToArray();

            var total = 0;
            ulong totalSeedingSize = 0, totalUploaded = 0, totalDownloaded = 0;
            foreach (var torrent in snap) {
                total++;

                if (torrent.InternalState.HasFlag(Shared.Abstractions.TORRENT_STATE.SEEDING))
                    totalSeedingSize += torrent.WantedSize;

                totalUploaded += torrent.Uploaded;
                totalDownloaded += torrent.Downloaded;
            }

            TotalTorrents = total;
            TotalSeedingSize = totalSeedingSize;
            TotalUploaded = totalUploaded;
            ShareRatio = (float)totalUploaded / totalDownloaded;

            return true;
        }

        ~StatisticsWindowViewModel()
        {
            Timer.Dispose();
        }
    }
}
