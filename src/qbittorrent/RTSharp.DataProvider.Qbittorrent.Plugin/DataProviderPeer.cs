using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Abstractions.Client;

using System.Net;

namespace RTSharp.DataProvider.Qbittorrent.Plugin
{
    public class DataProviderPeer : IDataProviderPeer
    {
        private Plugin ThisPlugin { get; }
        public IDataProviderHost<QbittorrentDataProviderInstanceConfig> Host => ThisPlugin.Host;

        public DataProviderPeerCapabilities Capabilities { get; } = new(
            AddPeer: true,
            BanPeer: true
        );

        public DataProviderPeer(Plugin ThisPlugin)
        {
            this.ThisPlugin = ThisPlugin;
        }

        public async Task AddPeer(Torrent Torrent, IPEndPoint Peer, CancellationToken cancellationToken = default)
        {
            var client = Host.AttachedDaemonService!.GetTorrentsService(Host.Instance);

            await client.AddPeer(Torrent.Hash, Peer, cancellationToken);
        }

        public async Task BanPeer(Torrent Torrent, IPEndPoint Peer, CancellationToken cancellationToken = default)
        {
            var client = Host.AttachedDaemonService!.GetTorrentsService(Host.Instance);

            await client.BanPeer(Torrent.Hash, Peer, cancellationToken);
        }
    }
}
