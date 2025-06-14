using Transmission.Net.Core.Entity.Torrent;

namespace Transmission.Net.Api.Entity.Torrent;

public class TorrentPeersFrom : ITorrentPeersFrom
{
    public int FromDHT { get; set; }
    public int FromIncoming { get; set; }
    public int FromLPD { get; set; }
    public int FromLTEP { get; set; }
    public int FromPEX { get; set; }
    public int FromTracker { get; set; }
}
