using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Abstractions.DataProvider;

namespace RTSharp.DataProvider.Qbittorrent.Plugin
{
    public class DataProviderTracker : IDataProviderTracker
    {
        private Plugin ThisPlugin { get; }
        public IDataProviderHost<QbittorrentDataProviderInstanceConfig> Host => ThisPlugin.Host;

        public DataProviderTrackerCapabilities Capabilities { get; } = new(
            AddNewTracker: false,
            EnableTracker: false,
            DisableTracker: false,
            RemoveTracker: true,
            ReannounceTracker: false,
            ReplaceTracker: true
        );

        public DataProviderTracker(Plugin ThisPlugin)
        {
            this.ThisPlugin = ThisPlugin;
        }

        public async Task<IList<(Uri Uri, IList<Exception> Exceptions)>> Reannounce(byte[] TorrentHash, IList<Uri> TargetUris)
        {
            throw new NotImplementedException();
        }

        public async Task ReplaceTracker(Torrent Torrent, string Existing, string New, CancellationToken cancellationToken = default)
        {
            var client = Host.AttachedDaemonService!.GetTorrentsService(Host.Instance);

            await client.ReplaceTracker(Torrent.Hash, Existing, New, cancellationToken);
        }

        public async Task AddNewTracker(Torrent Torrent, string New, CancellationToken cancellationToken = default)
        {
            var client = Host.AttachedDaemonService!.GetTorrentsService(Host.Instance);

            await client.AddNewTracker(Torrent.Hash, New, cancellationToken);
        }

        public async Task RemoveTracker(Torrent Torrent, string Existing, CancellationToken cancellationToken = default)
        {
            var client = Host.AttachedDaemonService!.GetTorrentsService(Host.Instance);

            await client.RemoveTracker(Torrent.Hash, Existing, cancellationToken);
        }
    }
}
