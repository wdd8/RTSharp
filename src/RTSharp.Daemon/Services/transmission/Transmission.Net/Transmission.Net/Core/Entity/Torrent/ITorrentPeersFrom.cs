using Newtonsoft.Json;

namespace Transmission.Net.Core.Entity.Torrent;

/// <summary>
/// Torrent peers from
/// </summary>
public interface ITorrentPeersFrom
{
    /// <summary>
    /// From DHT
    /// </summary>
    [JsonProperty("fromDht")]
    int FromDHT { get; set; }

    /// <summary>
    /// From incoming
    /// </summary>
    [JsonProperty("fromIncoming")]
    int FromIncoming { get; set; }

    /// <summary>
    /// From LPD
    /// </summary>
    [JsonProperty("fromLpd")]
    int FromLPD { get; set; }

    /// <summary>
    /// From LTEP
    /// </summary>
    [JsonProperty("fromLtep")]
    int FromLTEP { get; set; }

    /// <summary>
    /// From PEX
    /// </summary>
    [JsonProperty("fromPex")]
    int FromPEX { get; set; }

    /// <summary>
    /// From tracker
    /// </summary>
    [JsonProperty("fromTracker")]
    int FromTracker { get; set; }
}
