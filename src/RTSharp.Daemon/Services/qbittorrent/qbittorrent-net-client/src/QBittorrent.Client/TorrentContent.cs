using System.Text.Json.Serialization;
using QBittorrent.Client.Converters;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents torrent content file info.
    /// </summary>
    public class TorrentContent
    {
        /// <summary>
        /// File name (including relative path)
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Size
        /// </summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>
        /// File progress (percentage/100)
        /// </summary>
        [JsonPropertyName("progress")]
        public double Progress { get; set; }

        /// <summary>
        /// File priority
        /// </summary>
        [JsonPropertyName("priority")]
        public TorrentContentPriority Priority { get; set; }

        /// <summary>
        /// True if file is seeding/complete
        /// </summary>
        [JsonPropertyName("is_seed")]
        public bool IsSeeding { get; set; }

        /// <summary>
        /// The range of the file.
        /// </summary>
        [JsonPropertyName("piece_range")]
        [JsonConverter(typeof(ArrayToRangeConverter))]
        public Range PieceRange { get; set; }

        /// <summary>
        /// Gets the file availability, calculated as <c>(available_pieces)/(total_pieces)</c>
        /// </summary>
        [JsonPropertyName("availability")]
        public double? Availability { get; set; }
        /// <summary>
        /// File index
        /// </summary>
        [JsonPropertyName("index")]
        public int? Index { get; set; }
    }
}
