using System.Text.Json.Serialization;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents QBittorrent log entry.
    /// </summary>
    public class TorrentLogEntry
    {
        /// <summary>
        /// Message Id
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// Message text
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }

        /// <summary>
        /// Message timestamp
        /// </summary>
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        /// <summary>
        /// Log entry severity.
        /// </summary>
        [JsonPropertyName("type")]
        public TorrentLogSeverity Severity { get; set; }
    }
}
