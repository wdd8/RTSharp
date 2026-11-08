using System.Text.Json.Serialization;
using QBittorrent.Client.Converters;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents global transfer info.
    /// </summary>
    public class GlobalTransferInfo
    {
        /// <summary>
        /// Global download rate (bytes/s)
        /// </summary>
        [JsonPropertyName("dl_info_speed")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? DownloadSpeed { get; set; }

        /// <summary>
        /// Data downloaded this session (bytes)
        /// </summary>
        [JsonPropertyName("dl_info_data")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? DownloadedData { get; set; }

        /// <summary>
        /// Download rate limit (bytes/s)
        /// </summary>
        [JsonPropertyName("dl_rate_limit")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? DownloadSpeedLimit { get; set; }

        /// <summary>
        /// Global upload rate (bytes/s)
        /// </summary>
        [JsonPropertyName("up_info_speed")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? UploadSpeed { get; set; }

        /// <summary>
        /// Data uploaded this session (bytes)
        /// </summary>
        [JsonPropertyName("up_info_data")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? UploadedData { get; set; }

        /// <summary>
        /// Upload rate limit (bytes/s)
        /// </summary>
        [JsonPropertyName("up_rate_limit")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? UploadSpeedLimit { get; set; }

        /// <summary>
        /// DHT nodes connected to
        /// </summary>
        [JsonPropertyName("dht_nodes")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? DhtNodes { get; set; }

        /// <summary>
        /// Connection status
        /// </summary>
        [JsonPropertyName("connection_status")]
        public ConnectionStatus? ConnectionStatus { get; set; }
    }
}
