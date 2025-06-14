using Newtonsoft.Json;
using Transmission.Net.Core.Enums;
using static Transmission.Net.Api.TorrentFields;

namespace Transmission.Net.Core.Entity;

/// <summary>
/// Common torrent attributes used as both for the <c>torrent-set</c>
/// method and from the <c>torrent-get</c> method
/// </summary>
public interface ITorrentData
{
    /// <summary>
    /// This torrent's bandwidth priority
    /// </summary>
    [JsonProperty(BANDWIDTH_PRIORITY)]
    Priority? BandwidthPriority { get; set; }

    /// <summary>
    /// Maximum download speed (KBps)
    /// </summary>
    [JsonProperty(DOWNLOAD_LIMIT)]
    int? DownloadLimit { get; set; }

    /// <summary>
    /// <see langword="true"/> if downloadLimit is honored
    /// </summary>
    [JsonProperty(DOWNLOAD_LIMITED)]
    bool? DownloadLimited { get; set; }

    /// <summary>
    /// <see langword="true"/> if session upload limits are honored
    /// </summary>
    [JsonProperty(HONORS_SESSION_LIMITS)]
    bool? HonorsSessionLimits { get; set; }

    /// <summary>
    /// Labels
    /// </summary>
    [JsonProperty(LABELS)]
    string[]? Labels { get; set; }

    /// <summary>
    /// Maximum number of peers
    /// </summary>
    [JsonProperty(PEER_LIMIT)]
    int? PeerLimit { get; set; }

    /// <summary>
    /// Queue position
    /// </summary>
    [JsonProperty(QUEUE_POSITION)]
    int? QueuePosition { get; set; }

    /// <summary>
    /// Torrent-level number of minutes of seeding inactivity
    /// </summary>
    [JsonProperty(SEED_IDLE_LIMIT)]
    int? SeedIdleLimit { get; set; }

    /// <summary>
    /// Which seeding inactivity to use. See tr_idlelimit
    /// TODO: Add tr_idlelimit enum
    /// </summary>
    [JsonProperty(SEED_IDLE_MODE)]
    int? SeedIdleMode { get; set; }

    /// <summary>
    /// Torrent-level seeding ratio limit
    /// </summary>
    [JsonProperty(SEED_RATIO_LIMIT)]
    double? SeedRatioLimit { get; set; }

    /// <summary>
    /// Which ratio to use. See tr_ratiolimit
    /// TODO: Add tr_ratiolimit enum
    /// </summary>
    [JsonProperty(SEED_RATIO_MODE)]
    int? SeedRatioMode { get; set; }

    /// <summary>
    /// A string of announce URLs, one per line, with a blank
    /// line between tiers
    /// </summary>
    [JsonProperty(TRACKER_LIST)]
    string? TrackerList { get; set; }

    /// <summary>
    /// Maximum upload speed (KBps)
    /// </summary>
    [JsonProperty(UPLOAD_LIMIT)]
    int? UploadLimit { get; set; }

    /// <summary>
    /// <see langword="true"/> if <see cref="UploadLimit"/> is honored
    /// </summary>
    [JsonProperty(UPLOAD_LIMITED)]
    bool? UploadLimited { get; set; }
}
