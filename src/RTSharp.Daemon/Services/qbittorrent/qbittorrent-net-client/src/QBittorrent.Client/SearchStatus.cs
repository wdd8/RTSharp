using System.Text.Json.Serialization;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents search status.
    /// </summary>
    public class SearchStatus
    {
        /// <summary>
        /// ID of the search job.
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// Current status of the search job.
        /// </summary>
        [JsonPropertyName("status")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SearchJobStatus Status { get; set; }

        /// <summary>
        /// Total number of results.
        /// If the status is <see cref="SearchJobStatus.Running"/> this number may continue to increase.
        /// </summary>
        [JsonPropertyName("total")]
        public int Total { get; set; }
    }
}
