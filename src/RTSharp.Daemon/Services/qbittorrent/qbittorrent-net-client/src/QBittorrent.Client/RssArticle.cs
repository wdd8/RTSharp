using System;
using System.Text.Json.Serialization;
using QBittorrent.Client.Converters;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents an RSS article.
    /// </summary>
    public class RssArticle
    {
        /// <summary>
        /// The article id.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The article date/time.
        /// </summary>
        [JsonPropertyName("date")]
        public DateTimeOffset Date { get; set; }

        /// <summary>
        /// The title.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; }

        /// <summary>
        /// The author.
        /// </summary>
        [JsonPropertyName("author")]
        public string Author { get; set; }

        /// <summary>
        /// The description.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The torrent URI.
        /// </summary>
        [JsonPropertyName("torrentURL")]
        [JsonConverter(typeof(NullableUriConverter))]
        public Uri TorrentUri { get; set; }

        /// <summary>
        /// The torrent link.
        /// </summary>
        [JsonPropertyName("link")]
        [JsonConverter(typeof(NullableUriConverter))]
        public Uri Link { get; set; }

        /// <summary>
        /// The value indicating whether the article has been read.
        /// </summary>
        [JsonPropertyName("isRead")]
        public bool IsRead { get; set; }    }
}
