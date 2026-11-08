using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using QBittorrent.Client.Converters;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents a search result.
    /// </summary>
    public class SearchResult
    {
        /// <summary>
        /// Name of the file.
        /// </summary>
        [JsonPropertyName("fileName")]
        public string FileName { get; set; }

        /// <summary>
        /// Size of the file in bytes.
        /// </summary>
        [JsonPropertyName("fileSize")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? FileSize { get; set; }

        /// <summary>
        /// Torrent download link (usually either .torrent file or magnet link).
        /// </summary>
        [JsonPropertyName("fileUrl")]
        public Uri FileUrl { get; set; }

        /// <summary>
        /// Number of leechers.
        /// </summary>
        [JsonPropertyName("nbLeechers")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? Leechers { get; set; }

        /// <summary>
        /// Number of seeders.
        /// </summary>
        [JsonPropertyName("nbSeeders")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public long? Seeds { get; set; }

        /// <summary>
        /// URL of the torrent site
        /// </summary>
        [JsonPropertyName("siteUrl")]
        public Uri SiteUrl { get; set; }

        /// <summary>
        /// URL of the torrent's description page.
        /// </summary>
        [JsonPropertyName("descrLink")]
        public Uri DescriptionUrl { get; set; }
    }
}
