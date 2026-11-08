using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents current search results of a search job.
    /// </summary>
    public class SearchResults
    {
        /// <summary>
        /// Search results.
        /// </summary>
        [JsonPropertyName("results")]
        public IReadOnlyList<SearchResult> Results { get; set; }
        
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
