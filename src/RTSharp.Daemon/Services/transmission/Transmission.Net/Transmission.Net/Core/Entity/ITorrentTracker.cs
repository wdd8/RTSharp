using Newtonsoft.Json;

namespace Transmission.Net.Core.Entity;

/// <summary>
/// Torrent trackers
/// </summary>
public interface ITorrentTracker
{
    /// <summary>
    /// Full announce URL
    /// </summary>
    [JsonProperty("announce")]
    string Announce { get; set; }

    /// <summary>
    /// Full scrape URL
    /// </summary>
    [JsonProperty("scrape")]
    string Scrape { get; set; }

    /// <summary>
    /// Unique transmission-generated ID for use in libtransmission API
    /// </summary>
    [JsonProperty("id")]
    int Id { get; set; }

    /// <summary>
    /// Which tier this tracker is in
    /// </summary>
    [JsonProperty("tier")]
    int Tier { get; set; }
}
