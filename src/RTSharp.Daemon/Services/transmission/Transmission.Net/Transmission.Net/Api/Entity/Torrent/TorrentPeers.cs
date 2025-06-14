using Transmission.Net.Core.Entity.Torrent;

namespace Transmission.Net.Api.Entity.Torrent;

public class TorrentPeers : ITorrentPeers
{
    public string? Address { get; set; }
    public string? ClientName { get; set; }
    public bool? ClientIsChoked { get; set; }
    public bool? ClientIsInterested { get; set; }
    public string? FlagStr { get; set; }
    public bool? IsDownloadingFrom { get; set; }
    public bool? IsEncrypted { get; set; }
    public bool? IsUploadingTo { get; set; }
    public bool? IsUTP { get; set; }
    public bool? PeerIsChoked { get; set; }
    public bool? PeerIsInterested { get; set; }
    public int? Port { get; set; }
    public double? Progress { get; set; }
    public int? RateToClient { get; set; }
    public int? RateToPeer { get; set; }
}
