using Transmission.Net.Core.Entity.Torrent;
using Transmission.Net.Core.Enums;

namespace Transmission.Net.Api.Entity.Torrent;

public class TorrentFileStats : ITorrentFileStats
{
    public double BytesCompleted { get; set; }
    public bool Wanted { get; set; }
    public Priority Priority { get; set; }
}
