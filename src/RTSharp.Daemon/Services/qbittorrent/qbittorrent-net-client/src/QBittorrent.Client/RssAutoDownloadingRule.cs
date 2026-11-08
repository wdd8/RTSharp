using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using QBittorrent.Client.Converters;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents an RSS auto-downloading rule.
    /// </summary>
    public class RssAutoDownloadingRule
    {      
        /// <summary>
        /// Gets or sets the value indicating whether the rule is enabled.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        /// <summary>
        /// The substring that the torrent name must contain.
        /// </summary>
        /// <seealso cref="MustNotContain"/>
        /// <seealso cref="UseRegex"/>
        [JsonPropertyName("mustContain")]
        public string MustContain { get; set; } = string.Empty;

        /// <summary>
        /// The substring that the torrent name must not contain.
        /// </summary>
        /// <seealso cref="MustContain"/>
        /// <seealso cref="UseRegex"/>
        [JsonPropertyName("mustNotContain")]
        public string MustNotContain { get; set; } = string.Empty;

        /// <summary>
        /// Set to <see langword="true" /> in order to use regular expressions in 
        /// for <see cref="MustContain"/> and <see cref="MustNotContain"/> properties.
        /// </summary>
        [JsonPropertyName("useRegex")]
        public bool UseRegex { get; set; }

        /// <summary>
        /// Episode filter definition.
        /// </summary>
        [JsonPropertyName("episodeFilter")]
        public string EpisodeFilter { get; set; } = string.Empty;

        /// <summary>
        /// Enable smart episode filter.
        /// </summary>
        [JsonPropertyName("smartFilter")]
        public bool SmartFilter { get; set; }

        /// <summary>
        /// The list of episode IDs already matched by smart filter.
        /// </summary>
        [JsonPropertyName("previouslyMatchedEpisodes")]
        public IReadOnlyList<string> PreviouslyMatchedEpisodes { get; set; } = Array.Empty<string>();

        /// <summary>
        /// The feed URLs the rule applied to.
        /// </summary>
        [JsonPropertyName("affectedFeeds")]
        public IReadOnlyList<Uri> AffectedFeeds { get; set; } = Array.Empty<Uri>();

        /// <summary>
        /// Ignore subsequent rule matches.
        /// </summary>
        [JsonPropertyName("ignoreDays")]
        public int IgnoreDays { get; set; }

        /// <summary>
        /// The rule last match time.
        /// </summary>
        [JsonPropertyName("lastMatch")]
        [JsonConverter(typeof(Rfc2822ToDateTimeOffsetConverter))]
        // dd MMM yyyy HH:mm:ss + en-US culture
        public DateTimeOffset? LastMatch { get; set; }

        /// <summary>
        /// Add matched torrent in paused mode.
        /// </summary>
        [JsonPropertyName("addPaused")]
        public bool? AddPaused { get; set; }

        /// <summary>
        /// Category to assign to the torrent.
        /// </summary>
        [JsonPropertyName("assignedCategory")]
        public string AssignedCategory { get; set; } = string.Empty;

        /// <summary>
        /// The directory to save the torrent to.
        /// </summary>
        [JsonPropertyName("savePath")]
        public string SavePath { get; set; } = string.Empty;    }
}
