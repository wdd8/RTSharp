#nullable enable

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Abstractions.Client;

using System.Net;

namespace RTSharp.DataProvider.Transmission.Plugin;

public class DataProviderPeer : IDataProviderPeer
{
    private Plugin ThisPlugin { get; }

    public DataProviderPeerCapabilities Capabilities { get; } = new(
        AddPeer: false,
        BanPeer: false
    );

    public DataProviderPeer(Plugin ThisPlugin)
    {
        this.ThisPlugin = ThisPlugin;
    }

    public Task AddPeer(Torrent Torrent, IPEndPoint Peer, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task BanPeer(Torrent Torrent, IPEndPoint Peer, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
