using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using QBittorrent.Client.Converters;
using QBittorrent.Client.Internal;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents the changes in information about torrent since the last refresh.
    /// </summary>
    /// <seealso cref="PartialData"/>
    /// <seealso cref="QBittorrentClient.GetPartialDataAsync"/>
    [DebuggerDisplay("{" + nameof(Name) + "}")]
    public class TorrentPartialInfo
    {
        /// <summary>
        /// Torrent v1 (SHA-1) info hash. Available on qBittorrent 4.4+ (WebUI API >= 2.8.4); null otherwise or on partial rows that don't include it.
        /// </summary>
        [JsonPropertyName("infohash_v1")]
        public string InfoHashV1 { get; set; }

        /// <summary>
        /// Torrent v2 (SHA-256) info hash. Available on qBittorrent 4.4+ (WebUI API >= 2.8.4); null for v1-only torrents, older servers, or partial rows that don't include it.
        /// </summary>
        [JsonPropertyName("infohash_v2")]
        public string InfoHashV2 { get; set; }

        /// <summary>
        /// Torrent name
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Magnet URI for the torrent
        /// </summary>
        [JsonPropertyName("magnet_uri")]
        public string MagnetUri { get; set; }

        /// <summary>
        /// Total size (bytes) of files selected for download
        /// </summary>
        [JsonPropertyName("size")]
        public long? Size { get; set; }

        /// <summary>
        /// Torrent progress (percentage/100)
        /// </summary>
        [JsonPropertyName("progress")]
        public double? Progress { get; set; }

        /// <summary>
        /// Torrent download speed (bytes/s)
        /// </summary>
        [JsonPropertyName("dlspeed")]
        public long? DownloadSpeed { get; set; }

        /// <summary>
        /// Torrent upload speed (bytes/s)
        /// </summary>
        [JsonPropertyName("upspeed")]
        public long? UploadSpeed { get; set; }

        /// <summary>
        /// Torrent priority. Returns -1 if queuing is disabled or torrent is in seed mode
        /// </summary>
        [JsonPropertyName("priority")]
        public int? Priority { get; set; }

        /// <summary>
        /// Number of seeds connected to
        /// </summary>
        [JsonPropertyName("num_seeds")]
        public int? ConnectedSeeds { get; set; }

        /// <summary>
        /// Number of seeds in the swarm
        /// </summary>
        [JsonPropertyName("num_complete")]
        public int? TotalSeeds { get; set; }

        /// <summary>
        /// Number of leechers connected to
        /// </summary>
        [JsonPropertyName("num_leechs")]
        public int? ConnectedLeechers { get; set; }

        /// <summary>
        /// Number of leechers in the swarm
        /// </summary>
        [JsonPropertyName("num_incomplete")]
        public int? TotalLeechers { get; set; }

        /// <summary>
        /// Torrent share ratio. Max ratio value is <c>9999</c>. If actual ratio is greater than <c>9999</c>, <c>-1</c> is returned.
        /// </summary>
        [JsonPropertyName("ratio")]
        public double? Ratio { get; set; }

        /// <summary>
        /// Torrent ETA
        /// </summary>
        [JsonPropertyName("eta")]
        [JsonConverter(typeof(SecondsToTimeSpanConverter))]
        public TimeSpan? EstimatedTime { get; set; }

        /// <summary>
        /// Torrent state
        /// </summary>
        [JsonPropertyName("state")]
        [JsonConverter(typeof(TorrentStateConverter))]
        public TorrentState? State { get; set; }

        /// <summary>
        /// True if sequential download is enabled
        /// </summary>
        [JsonPropertyName("seq_dl")]
        public bool? SequentialDownload { get; set; }

        /// <summary>
        /// True if first last piece are prioritized
        /// </summary>
        [JsonPropertyName("f_l_piece_prio")]
        public bool? FirstLastPiecePrioritized { get; set; }

        /// <summary>
        /// Category of the torrent
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; }

        /// <summary>
        /// The torrent tags
        /// </summary>
        [JsonPropertyName("tags")]
        [StringToList(",", true)]
        public IReadOnlyCollection<string> Tags { get; set; }

        /// <summary>
        /// True if super seeding is enabled
        /// </summary>
        [JsonPropertyName("super_seeding")]
        public bool? SuperSeeding { get; set; }

        /// <summary>
        /// True if force start is enabled for this torrent
        /// </summary>
        [JsonPropertyName("force_start")]
        public bool? ForceStart { get; set; }

        /// <summary>
        /// The torrent save path
        /// </summary>
        [JsonPropertyName("save_path")]
        public string SavePath { get; set; }

        /// <summary>
        /// The date and time when the torrent was added
        /// </summary>
        [JsonPropertyName("added_on")]
        [JsonConverter(typeof(UnixTimeToNullableDateTimeConverter))]
        public DateTime? AddedOn { get; set; }

        /// <summary>
        /// The date and time when the torrent was completed
        /// </summary>
        [JsonPropertyName("completion_on")]
        [JsonConverter(typeof(UnixTimeToNullableDateTimeConverter))]
        public DateTime? CompletionOn { get; set; }

        /// <summary>
        /// The current torrent tracker
        /// </summary>
        [JsonPropertyName("tracker")]
        public string CurrentTracker { get; set; }

        /// <summary>
        /// The download speed limit (bytes/second)
        /// </summary>
        [JsonPropertyName("dl_limit")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public int? DownloadLimit { get; set; }

        /// <summary>
        /// The upload speed limit (bytes/second)
        /// </summary>
        [JsonPropertyName("up_limit")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public int? UploadLimit { get; set; }

        /// <summary>
        /// The downloaded data size (bytes)
        /// </summary>
        [JsonPropertyName("downloaded")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? Downloaded { get; set; }

        /// <summary>
        /// The uploaded data size (bytes)
        /// </summary>
        [JsonPropertyName("uploaded")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? Uploaded { get; set; }

        /// <summary>
        /// The downloaded in this session data size (bytes)
        /// </summary>
        [JsonPropertyName("downloaded_session")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? DownloadedInSession { get; set; }

        /// <summary>
        /// The uploaded in this session data size (bytes)
        /// </summary>
        [JsonPropertyName("uploaded_session")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? UploadedInSession { get; set; }

        /// <summary>
        /// The remaining amount to download (bytes)
        /// </summary>
        [JsonPropertyName("amount_left")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? IncompletedSize { get; set; }

        /// <summary>
        /// The completed amount (bytes)
        /// </summary>
        [JsonPropertyName("completed")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? CompletedSize { get; set; }

        /// <summary>
        /// The maximal allowed ratio
        /// </summary>
        [JsonPropertyName("ratio_limit")]
        public double? RatioLimit { get; set; }

        /// <summary>
        /// Upload seeding time limit 
        /// </summary>
        [JsonPropertyName("seeding_time_limit")]
        [JsonConverter(typeof(MinutesToTimeSpanConverter))]
        public TimeSpan? SeedingTimeLimit { get; set; }

        /// <summary>
        /// Upload inactive seeding time limit 
        /// </summary>
        [JsonPropertyName("inactive_seeding_time_limit")]
        [JsonConverter(typeof(MinutesToTimeSpanConverter))]
        public TimeSpan? InactiveSeedingTimeLimit { get; set; }

        /// <summary>
        /// The date and time when this torrent was seen complete for the last time
        /// </summary>
        [JsonPropertyName("seen_complete")]
        [JsonConverter(typeof(UnixTimeToNullableDateTimeConverter))]
        public DateTime? LastSeenComplete { get; set; }

        /// <summary>
        /// The date and time when this torrent was seen active for the last time
        /// </summary>
        [JsonPropertyName("last_activity")]
        [JsonConverter(typeof(UnixTimeToNullableDateTimeConverter))]
        public DateTime? LastActivityTime { get; set; }

        /// <summary>
        /// The active time of this torrent
        /// </summary>
        [JsonPropertyName("time_active")]
        [JsonConverter(typeof(SecondsToTimeSpanConverter))]
        public TimeSpan? ActiveTime { get; set; }

        /// <summary>
        /// Indicates whether automatic torrent management is enabled.
        /// </summary>
        [JsonPropertyName("auto_tmm")]
        public bool? AutomaticTorrentManagement { get; set; }

        /// <summary>
        /// Total torrent size (bytes)
        /// </summary>
        [JsonPropertyName("total_size")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? TotalSize { get; set; }

        /// <summary>
        /// True if the torrent is private (no DHT/PeX/LSD). Available on qBittorrent 5.0+; null on older
        /// servers, on torrents whose metadata has not been downloaded yet, and on partial rows that
        /// don't include it.
        /// </summary>
        [JsonPropertyName("private")]
        public bool? Private { get; set; }

        /// <summary>
        /// True if at least one tracker announce returned a warning. Only reported by <c>sync/maindata</c>
        /// on qBittorrent 5.1+ (WebUI API >= 2.11.7); null otherwise or on partial rows that don't include it.
        /// </summary>
        [JsonPropertyName("has_tracker_warning")]
        public bool? HasTrackerWarning { get; set; }

        /// <summary>
        /// True if at least one tracker responded to an announce with an error. Only reported by
        /// <c>sync/maindata</c> on qBittorrent 5.1+ (WebUI API >= 2.11.7); null otherwise or on partial
        /// rows that don't include it.
        /// </summary>
        [JsonPropertyName("has_tracker_error")]
        public bool? HasTrackerError { get; set; }

        /// <summary>
        /// True if at least one tracker could not be announced to at all (unreachable/not working). Only
        /// reported by <c>sync/maindata</c> on qBittorrent 5.1+ (WebUI API >= 2.11.7); null otherwise or on
        /// partial rows that don't include it.
        /// </summary>
        [JsonPropertyName("has_other_announce_error")]
        public bool? HasOtherAnnounceError { get; set; }

        /// <inheritdoc />
        public override string ToString()
        {
            return JsonSerializer.Serialize(this, QBittorrentJson.DisplayOptions);
        }
    }
}
