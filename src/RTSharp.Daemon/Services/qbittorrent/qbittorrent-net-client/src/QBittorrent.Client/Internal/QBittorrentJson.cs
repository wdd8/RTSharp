using System.Text.Json;
using QBittorrent.Client.Converters;

namespace QBittorrent.Client.Internal
{
    internal static class QBittorrentJson
    {
        /// <summary>
        /// Reads numbers given as strings, and enums as numbers or names.
        /// </summary>
        public static readonly JsonSerializerOptions Options = new()
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
            Converters = { new NumberOrNameEnumConverter() }
        };

        /// <summary>
        /// For RSS feeds and articles, which have RFC 822 dates and braced IDs.
        /// </summary>
        public static readonly JsonSerializerOptions RssOptions = new(Options)
        {
            Converters = { new RssDateTimeOffsetConverter(), new AnyFormatGuidConverter() }
        };

        /// <summary>
        /// For readable output of objects, leaving out default values.
        /// </summary>
        public static readonly JsonSerializerOptions DisplayOptions = new(Options)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
        };
    }
}
