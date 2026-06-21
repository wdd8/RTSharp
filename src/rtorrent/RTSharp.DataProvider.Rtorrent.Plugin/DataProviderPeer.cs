#nullable enable

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Abstractions.Client;

using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.DataProvider.Rtorrent.Plugin;

public class DataProviderPeer : IDataProviderPeer
{
    private Plugin ThisPlugin { get; }
    public IPluginHost PluginHost { get; }

    public DataProviderPeerCapabilities Capabilities { get; } = new(
        AddPeer: true,
        BanPeer: true
    );

    public DataProviderPeer(Plugin ThisPlugin)
    {
        this.ThisPlugin = ThisPlugin;
        this.PluginHost = ThisPlugin.Host;
    }

    public async Task AddPeer(Torrent Torrent, IPEndPoint Peer, CancellationToken cancellationToken = default)
    {
        var client = PluginHost.AttachedDaemonService!.GetTorrentsService(ThisPlugin.DataProvider.Instance);

        await client.AddPeer(Torrent.Hash, Peer, cancellationToken);
    }

    public async Task BanPeer(Torrent Torrent, IPEndPoint Peer, CancellationToken cancellationToken = default)
    {
        var client = PluginHost.AttachedDaemonService!.GetTorrentsService(ThisPlugin.DataProvider.Instance);

        await client.BanPeer(Torrent.Hash, Peer, cancellationToken);
    }
}
