using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Transmission.Net.Core.Enums;

namespace Transmission.Net.Core.Entity;

/// <summary>
/// Torrent tracker stats
/// </summary>
public interface ITorrentTrackerStats
{
    /// <inheritdoc cref="ITorrentTracker.Announce"/>
    [JsonProperty("announce")]
    string? Announce { get; set; }

    /// <inheritdoc cref="ITorrentTracker.Scrape"/>
    [JsonProperty("scrape")]
    string? Scrape { get; set; }

    /// <summary>
    /// Uniquely-identifying tracker name ({host}:{port})
    /// </summary>
    [JsonProperty("host")]
    string? Host { get; set; }


    /// <summary>
    /// If <see cref="HasAnnounced"/>, the human-readable result of latest announce
    /// </summary>
    [JsonProperty("lastAnnounceResult")]
    string? LastAnnounceResult { get; set; }

    /// <summary>
    /// If <see cref="HasScraped"/>, the human-readable result of the latest scrape
    /// </summary>
    [JsonProperty("lastScrapeResult")]
    string? LastScrapeResult { get; set; }


    /// <summary>
    /// If <see cref="HasAnnounced"/>, when the latest announce request was sent
    /// </summary>
    [JsonProperty("lastAnnounceStartTime"), JsonConverter(typeof(UnixDateTimeConverter))]
    DateTime? LastAnnounceStartTime { get; set; }

    /// <summary>
    /// If <see cref="HasAnnounced"/>, when the latest announce reply was received
    /// </summary>
    [JsonProperty("lastAnnounceTime"), JsonConverter(typeof(UnixDateTimeConverter))]
    DateTime? LastAnnounceTime { get; set; }

    /// <summary>
    /// If <see cref="AnnounceState"/> == <see cref="TrackerState.Waiting"/>, time of next announce
    /// </summary>
    [JsonProperty("nextAnnounceTime"), JsonConverter(typeof(UnixDateTimeConverter))]
    DateTime? NextAnnounceTime { get; set; }

    /// <summary>
    /// If <see cref="HasScraped"/>, when the latest scrape request was sent
    /// </summary>
    [JsonProperty("lastScrapeStartTime"), JsonConverter(typeof(UnixDateTimeConverter))]
    DateTime? LastScrapeStartTime { get; set; }

    /// <summary>
    /// If <see cref="HasScraped"/>, when the latest scrape reply was received
    /// </summary>
    [JsonProperty("lastScrapeTime"), JsonConverter(typeof(UnixDateTimeConverter))]
    DateTime? LastScrapeTime { get; set; }

    /// <summary>
    /// If <see cref="ScrapeState"/> == <see cref="TrackerState.Waiting"/>, time of next scrape
    /// </summary>
    [JsonProperty("nextScrapeTime"), JsonConverter(typeof(UnixDateTimeConverter))]
    DateTime? NextScrapeTime { get; set; }


    /// <summary>
    /// Number of times this torrent's been downloaded, or -1 if unknown
    /// </summary>
    [JsonProperty("downloadCount")]
    int? DownloadCount { get; set; }

    /// <summary>
    /// If <see cref="HasAnnounced"/>, the number of peers the tracker gave us
    /// </summary>
    [JsonProperty("lastAnnouncePeerCount")]
    int? LastAnnouncePeerCount { get; set; }

    /// <summary>
    /// Number of leechers the tracker knows of, or -1 if unknown
    /// </summary>
    [JsonProperty("leecherCount")]
    int? LeecherCount { get; set; }

    /// <summary>
    /// Number of seeders the tracker knows of, or -1 if unknown
    /// </summary>
    [JsonProperty("seederCount")]
    int? SeederCount { get; set; }


    /// <inheritdoc cref="ITorrentTracker.Tier"/>
    [JsonProperty("tier")]
    int? Tier { get; set; }

    /// <inheritdoc cref="ITorrentTracker.Id"/>
    [JsonProperty("id")]
    int? Id { get; set; }


    /// <summary>
    /// Whether we're announcing, waiting to announce, etc.
    /// </summary>
    [JsonProperty("announceState")]
    TrackerState? AnnounceState { get; set; }

    /// <summary>
    /// Whether we're scraping, waiting to scrape, etc.
    /// </summary>
    [JsonProperty("scrapeState")]
    TrackerState? ScrapeState { get; set; }


    /// <summary>
    /// <see langword="true"/> if we've announced to this tracker during this session
    /// </summary>
    [JsonProperty("hasAnnounced")]
    bool? HasAnnounced { get; set; }

    /// <summary>
    /// <see langword="true"/> if we've scraped this tracker during this session
    /// </summary>
    [JsonProperty("hasScraped")]
    bool? HasScraped { get; set; }

    /// <summary>
    /// Only one tracker per tier is used; the others are kept as backups
    /// </summary>
    [JsonProperty("isBackup")]
    bool? IsBackup { get; set; }

    /// <summary>
    /// If <see cref="HasAnnounced"/>, whether or not the latest announce succeeded
    /// </summary>
    [JsonProperty("lastAnnounceSucceeded")]
    bool? LastAnnounceSucceeded { get; set; }

    /// <summary>
    /// <see langword="true"/> if the latest announce request timed out
    /// </summary>
    [JsonProperty("lastAnnounceTimedOut")]
    bool? LastAnnounceTimedOut { get; set; }

    /// <summary>
    /// If <see cref="HasScraped"/>, whether or not the latest scrape succeeded
    /// </summary>
    [JsonProperty("lastScrapeSucceeded")]
    bool? LastScrapeSucceeded { get; set; }

    /// <summary>
    /// <see langword="true"/> if the latest scrape request timed out
    /// </summary>
    [JsonProperty("lastScrapeTimedOut")]
    bool? LastScrapeTimedOut { get; set; }
}
