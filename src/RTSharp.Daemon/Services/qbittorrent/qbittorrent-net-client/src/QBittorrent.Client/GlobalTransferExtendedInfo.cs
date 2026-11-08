using System.Text.Json.Serialization;
using QBittorrent.Client.Converters;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents global transfer info with additional information and statistics.
    /// </summary>
    public class GlobalTransferExtendedInfo : GlobalTransferInfo
    {
        /// <summary>
        /// The amount of data downloaded for all time (bytes)
        /// </summary>
        [JsonPropertyName("alltime_dl")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? AllTimeDownloaded { get; set; }

        /// <summary>
        /// The amount of data uploaded for all time (bytes)
        /// </summary>
        [JsonPropertyName("alltime_ul")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? AllTimeUploaded { get; set; }

        /// <summary>
        /// The ammount of data wasted (bytes)
        /// </summary>
        [JsonPropertyName("total_wasted_session")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? TotalWasted { get; set; }

        /// <summary>
        /// Total number of peer connections
        /// </summary>
        [JsonPropertyName("total_peer_connections")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? TotalPeerConnections { get; set; }
        
        /// <summary>
        /// Total used buffers size
        /// </summary>
        [JsonPropertyName("total_buffers_size")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? TotalBuffersSize { get; set; }

        /// <summary>
        /// Indicates whether the global alternative speed limits are enabled
        /// </summary>
        [JsonPropertyName("use_alt_speed_limits")]
        public bool GlobalAltSpeedLimitsEnabled { get; set; }

        /// <summary>
        /// The preferred refresh interval (milliseconds)
        /// </summary>
        [JsonPropertyName("refresh_interval")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public int? RefreshInterval { get; set; }

        /// <summary>
        /// Free space on disk.
        /// </summary>
        /// <remarks>This value is available starting from API v2.1.1.</remarks>
        [JsonPropertyName("free_space_on_disk")]
        [ApiLevel(ApiLevel.V2, MinVersion = "2.1.1")]
        public long? FreeSpaceOnDisk { get; set; }

        /// <summary>
        /// Global ratio
        /// </summary>
        [JsonIgnore]
        public double? GlobalRatio => AllTimeUploaded / (double?) AllTimeDownloaded;
    }
}
