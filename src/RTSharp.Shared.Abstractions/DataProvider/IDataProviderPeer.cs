using System.Net;

namespace RTSharp.Shared.Abstractions.DataProvider;

public interface IDataProviderPeer : IDataProviderBase<DataProviderPeerCapabilities>
{
    Task AddPeer(Torrent Torrent, IPEndPoint Peer, CancellationToken cancellationToken = default);
}
