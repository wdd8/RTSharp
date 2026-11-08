using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents information about a search plugin.
    /// </summary>
    public class SearchPlugin
    {
        /// <summary>
        /// Name that can be used to perform search using all plugins.
        /// </summary>
        public const string All = "all";

        /// <summary>
        /// Name that can be used to perform search using all enabled plugins.
        /// </summary>
        public const string Enabled = "enabled";

        /// <summary>
        /// Whether the plugin is enabled.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Full name of the plugin
        /// </summary>
        [JsonPropertyName("fullName")]
        public string FullName { get; set; }

        /// <summary>
        /// Short name of the plugin
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// List of supported categories
        /// </summary>
        [Obsolete("Use " + nameof(Categories) + " property instead.")]
        [JsonIgnore]
        public IReadOnlyList<string> SupportedCategories { get; set; } = Array.Empty<string>();

        /// <summary>
        /// List of supported categories
        /// </summary>
        [JsonIgnore]
        public IReadOnlyList<SearchPluginCategory> Categories { get; set; } = Array.Empty<SearchPluginCategory>();

#pragma warning disable 618
        [JsonPropertyName("supportedCategories")]
        [JsonInclude]
        [JsonConverter(typeof(CategoriesConverter))]
        private IReadOnlyList<SearchPluginCategory> CategoriesField
        {
            get => Categories;
            set
            {
                Categories = value ?? Array.Empty<SearchPluginCategory>();
                SupportedCategories = Categories.Select(c => c.Id).ToArray();
            }
        }
#pragma warning restore 618

        /// <summary>
        /// URL of the torrent site
        /// </summary>
        [JsonPropertyName("url")]
        public Uri Url { get; set; }

        /// <summary>
        /// Installed version of the plugin
        /// </summary>
        [JsonPropertyName("version")]
        public Version Version { get; set; }

        private sealed class CategoriesConverter : JsonConverter<IReadOnlyList<SearchPluginCategory>>
        {
            public override IReadOnlyList<SearchPluginCategory> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartArray)
                    throw new JsonException($"Unexpected token {reader.TokenType}.");

                var categories = new List<SearchPluginCategory>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    categories.Add(reader.TokenType == JsonTokenType.String ?
                        new SearchPluginCategory(reader.GetString()) :
                        JsonSerializer.Deserialize<SearchPluginCategory>(ref reader, options));
                }
                return categories;
            }

            public override void Write(Utf8JsonWriter writer, IReadOnlyList<SearchPluginCategory> value, JsonSerializerOptions options) =>
                JsonSerializer.Serialize(writer, value, options);
        }
    }
}
