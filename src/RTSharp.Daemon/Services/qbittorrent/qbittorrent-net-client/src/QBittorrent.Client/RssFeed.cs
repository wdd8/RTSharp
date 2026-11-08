using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents an RSS Feed.
    /// </summary>
    /// <seealso cref="QBittorrent.Client.RssItem" />
    [DebuggerDisplay("Feed {Name} : {Url}")]
    public class RssFeed : RssItem
    {
        /// <summary>
        /// Gets or sets the uid.
        /// </summary>
        /// <value>
        /// The uid.
        /// </value>
        [JsonPropertyName("uid")]
        public Guid Uid { get; set; }

        /// <summary>
        /// Gets or sets the feed URL.
        /// </summary>
        /// <value>
        /// The feed URL.
        /// </value>
        [JsonPropertyName("url")]
        public Uri Url { get; set; }

        /// <summary>
        /// Gets or sets the feed title.
        /// </summary>
        /// <value>
        /// The title.
        /// </value>
        [JsonPropertyName("title")]
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the last build date.
        /// </summary>
        /// <value>
        /// The last build date.
        /// </value>
        [JsonPropertyName("lastBuildDate")]
        public DateTimeOffset? LastBuildDate { get; set; }

        /// <summary>
        /// Gets or sets the value indicating whether the feed is loading.
        /// </summary>
        [JsonPropertyName("isLoading")]
        public bool? IsLoading { get; set; }

        /// <summary>
        /// Gets or sets the value indicating whether the feed has an error.
        /// </summary>
        [JsonPropertyName("hasError")]
        public bool? HasError { get; set; }

        /// <summary>
        /// Gets or sets the articles.
        /// </summary>
        /// <value>
        /// The articles.
        /// </value>
        [JsonPropertyName("articles")]
        public IList<RssArticle> Articles { get; set; }
    }
}
