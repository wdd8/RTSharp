using System;
using System.Text.Json.Serialization;
using QBittorrent.Client.Converters;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents additional torrent properties.
    /// </summary>
    /// <seealso cref="TorrentInfo"/>
    public class TorrentProperties
    {
        /// <summary>
        /// Torrent save path
        /// </summary>
        [JsonPropertyName("save_path")]
        public string SavePath { get; set; }

        /// <summary>
        /// Torrent creation date
        /// </summary>
        [JsonPropertyName("creation_date")]
        [JsonConverter(typeof(UnixTimeToNullableDateTimeConverter))]
        public DateTime? CreationDate { get; set; }

        /// <summary>
        /// Torrent comment
        /// </summary>
        [JsonPropertyName("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// Torrent total size (bytes)
        /// </summary>
        [JsonPropertyName("total_size")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? Size { get; set; }

        /// <summary>
        /// Total data wasted for torrent (bytes)
        /// </summary>
        [JsonPropertyName("total_wasted")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? TotalWasted { get; set; }

        /// <summary>
        /// Total data uploaded for torrent (bytes)
        /// </summary>
        [JsonPropertyName("total_uploaded")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? TotalUploaded { get; set; }

        /// <summary>
        /// Total data uploaded this session (bytes)
        /// </summary>
        [JsonPropertyName("total_uploaded_session")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? TotalUploadedInSession { get; set; }

        /// <summary>
        /// Total data downloaded for torrent (bytes)
        /// </summary>
        [JsonPropertyName("total_downloaded")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? TotalDownloaded { get; set; }

        /// <summary>
        /// Total data downloaded this session (bytes)
        /// </summary>
        [JsonPropertyName("total_downloaded_session")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? TotalDownloadedInSession { get; set; }

        /// <summary>
        /// Torrent upload limit (bytes/s)
        /// </summary>
        [JsonPropertyName("up_limit")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? UploadLimit { get; set; }

        /// <summary>
        /// Torrent download limit (bytes/s)
        /// </summary>
        [JsonPropertyName("dl_limit")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? DownloadLimit { get; set; }

        /// <summary>
        /// Torrent elapsed time
        /// </summary>
        [JsonPropertyName("time_elapsed")]
        [JsonConverter(typeof(SecondsToTimeSpanConverter))]
        public TimeSpan? TimeElapsed { get; set; }

        /// <summary>
        /// Torrent elapsed time while complete
        /// </summary>
        [JsonPropertyName("seeding_time")]
        [JsonConverter(typeof(SecondsToTimeSpanConverter))]
        public TimeSpan? SeedingTime { get; set; }

        /// <summary>
        /// Torrent connection count
        /// </summary>
        [JsonPropertyName("nb_connections")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public int? ConnectionCount { get; set; }

        /// <summary>
        /// Torrent connection count limit
        /// </summary>
        [JsonPropertyName("nb_connections_limit")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public int? ConnectionLimit { get; set; }

        /// <summary>
        /// Torrent share ratio
        /// </summary>
        [JsonPropertyName("share_ratio")]
        public double ShareRatio { get; set; }

        /// <summary>
        /// When this torrent was added
        /// </summary>
        [JsonPropertyName("addition_date")]
        [JsonConverter(typeof(UnixTimeToNullableDateTimeConverter))]
        public DateTime? AdditionDate { get; set; }

        /// <summary>
        /// Torrent completion date
        /// </summary>
        [JsonPropertyName("completion_date ")]
        [JsonConverter(typeof(UnixTimeToNullableDateTimeConverter))]
        public DateTime? CompletionDate { get; set; }

        /// <summary>
        /// Torrent creator
        /// </summary>
        [JsonPropertyName("created_by")]
        public string CreatedBy { get; set; }

        /// <summary>
        /// Torrent average download speed (bytes/second)
        /// </summary>
        [JsonPropertyName("dl_speed_avg")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? AverageDownloadSpeed { get; set; }

        /// <summary>
        /// Torrent download speed (bytes/second)
        /// </summary>
        [JsonPropertyName("dl_speed")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? DownloadSpeed { get; set; }

        /// <summary>
        /// Torrent average upload speed (bytes/second)
        /// </summary>
        [JsonPropertyName("up_speed_avg")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? AverageUploadSpeed { get; set; }

        /// <summary>
        /// Torrent upload speed (bytes/second)
        /// </summary>
        [JsonPropertyName("up_speed")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? UploadSpeed { get; set; }

        /// <summary>
        /// Torrent ETA
        /// </summary>
        [JsonPropertyName("eta")]
        [JsonConverter(typeof(SecondsToTimeSpanConverter))]
        public TimeSpan? EstimatedTime { get; set; }

        /// <summary>
        /// Last seen complete date
        /// </summary>
        [JsonPropertyName("last_seen")]
        [JsonConverter(typeof(UnixTimeToNullableDateTimeConverter))]
        public DateTime? LastSeen { get; set; }

        /// <summary>
        /// Number of peers connected to
        /// </summary>
        [JsonPropertyName("peers")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public int? Peers { get; set; }

        /// <summary>
        /// Number of peers in the swarm
        /// </summary>
        [JsonPropertyName("peers_total")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public int? TotalPeers { get; set; }

        /// <summary>
        /// Number of seeds connected to
        /// </summary>
        [JsonPropertyName("seeds")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public int? Seeds { get; set; }

        /// <summary>
        /// Number of seeds in the swarm
        /// </summary>
        [JsonPropertyName("seeds_total")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public int? TotalSeeds { get; set; }

        /// <summary>
        /// Torrent piece size
        /// </summary>
        [JsonPropertyName("piece_size")]
        public long? PieceSize { get; set; }

        /// <summary>
        /// Number of pieces owned
        /// </summary>
        [JsonPropertyName("pieces_have")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public int? OwnedPieces { get; set; }

        /// <summary>
        /// Number of pieces of the torrent
        /// </summary>
        [JsonPropertyName("pieces_num")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public int? TotalPieces { get; set; }

        /// <summary>
        /// Number of seconds until the next announce
        /// </summary>
        [JsonPropertyName("reannounce")]
        [JsonConverter(typeof(SecondsToTimeSpanConverter))]
        public TimeSpan? Reannounce { get; set; }    }
}
