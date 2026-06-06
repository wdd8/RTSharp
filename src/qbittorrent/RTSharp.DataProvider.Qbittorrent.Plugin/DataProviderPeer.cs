using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Abstractions.Client;

using System.Net;

namespace RTSharp.DataProvider.Qbittorrent.Plugin
{
    public class DataProviderPeer : IDataProviderPeer
    {
        private Plugin ThisPlugin { get; }
        public IPluginHost PluginHost { get; }

        public DataProviderPeerCapabilities Capabilities { get; } = new(
            AddPeer: true,
            BanPeer: false,
            KickPeer: false,
            SnubPeer: false,
            UnsnubPeer: false
        );

        public DataProviderPeer(Plugin ThisPlugin)
        {
            this.ThisPlugin = ThisPlugin;
            this.PluginHost = ThisPlugin.Host;
        }

        public async Task AddPeer(Torrent Torrent, IPEndPoint Peer, CancellationToken cancellationToken = default)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(ThisPlugin.DataProvider.Instance);

            await client.AddPeer(Torrent.Hash, Peer, cancellationToken);
        }
    }
}
