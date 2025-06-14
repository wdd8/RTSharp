using Newtonsoft.Json;

namespace Transmission.Net.Core.Entity.Torrent;

public interface ITorrentPeers
{
    /// <summary>
    /// Address
    /// </summary>
    [JsonProperty("address")]
    string? Address { get; set; }

    /// <summary>
    /// Client name
    /// </summary>
    [JsonProperty("clientName")]
    string? ClientName { get; set; }

    /// <summary>
    /// Client is choked
    /// </summary>
    [JsonProperty("clientIsChoked")]
    bool? ClientIsChoked { get; set; }

    /// <summary>
    /// Client is interested
    /// </summary>
    [JsonProperty("clientIsInterested")]
    bool? ClientIsInterested { get; set; }

    /// <summary>
    /// Flag string
    /// </summary>
    [JsonProperty("flagStr")]
    string? FlagStr { get; set; }

    /// <summary>
    /// Is downloading from
    /// </summary>
    [JsonProperty("isDownloadingFrom")]
    bool? IsDownloadingFrom { get; set; }

    /// <summary>
    /// Is encrypted
    /// </summary>
    [JsonProperty("isEncrypted")]
    bool? IsEncrypted { get; set; }

    /// <summary>
    /// Is uploading to
    /// </summary>
    [JsonProperty("isUploadingTo")]
    bool? IsUploadingTo { get; set; }

    /// <summary>
    /// Is UTP
    /// </summary>
    [JsonProperty("isUTP")]
    bool? IsUTP { get; set; }

    /// <summary>
    /// Peer is choked
    /// </summary>
    [JsonProperty("peerIsChoked")]
    bool? PeerIsChoked { get; set; }

    /// <summary>
    /// Peer is interested
    /// </summary>
    [JsonProperty("peerIsInterested")]
    bool? PeerIsInterested { get; set; }

    /// <summary>
    /// Port
    /// </summary>
    [JsonProperty("port")]
    int? Port { get; set; }

    /// <summary>
    /// Progress
    /// </summary>
    [JsonProperty("progress")]
    double? Progress { get; set; }

    /// <summary>
    /// Rate to client
    /// </summary>
    [JsonProperty("rateToClient")]
    int? RateToClient { get; set; }

    /// <summary>
    /// Rate to peer
    /// </summary>
    [JsonProperty("rateToPeer")]
    int? RateToPeer { get; set; }
}
