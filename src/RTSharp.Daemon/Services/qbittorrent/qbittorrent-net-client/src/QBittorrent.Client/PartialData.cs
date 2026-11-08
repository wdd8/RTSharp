using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents full or partial data returned by <see cref="QBittorrentClient.GetPartialDataAsync"/> method.
    /// </summary>
    public class PartialData : IJsonOnDeserialized
    {
        /// <summary>
        /// Gets or sets the response identifier.
        /// </summary>
        /// <value>
        /// The response identifier.
        /// </value>
        [JsonPropertyName("rid")]
        public int ResponseId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this object contains all data.
        /// </summary>
        /// <value>
        ///   <see langword="true" /> if this object contains all data; 
        ///   <see langword="false" /> if this object contains only changes of data since the previous request.
        /// </value>
        [JsonPropertyName("full_update")]
        public bool FullUpdate { get; set; }

        /// <summary>
        /// Gets or sets the list of changed or added torrents.
        /// </summary>
        [JsonPropertyName("torrents")]
        public IReadOnlyDictionary<string, TorrentPartialInfo> TorrentsChanged { get; set; }

        /// <summary>
        /// Gets or sets the list of removed torrents.
        /// </summary>
        [JsonPropertyName("torrents_removed")]
        public IReadOnlyList<string> TorrentsRemoved { get; set; }

        /// <summary>
        /// Gets or sets the list of added categories.
        /// </summary>
        /// <remarks>
        /// Starting from API v2.1.0 this property will always be <see langword="null"/>.
        /// You should use <see cref="CategoriesChanged"/> property instead.
        /// </remarks>
        /// <seealso cref="CategoriesChanged"/>
        [Obsolete("Use CategoriesChanged property instead.")]
        [JsonIgnore]
        public IReadOnlyList<string> CategoriesAdded { get; set; }

        /// <summary>
        /// Gets or sets the changed categories with their save paths.
        /// </summary>
        [JsonIgnore]
        public IReadOnlyDictionary<string, Category> CategoriesChanged { get; set; }

        /// <summary>
        /// Gets or sets the list of removed categories.
        /// </summary>
        [JsonPropertyName("categories_removed")]
        public IReadOnlyList<string> CategoriesRemoved { get; set; }
        
        /// <summary>
        /// Get or sets the list of added tags.
        /// </summary>
        [JsonPropertyName("tags")]
        public IReadOnlyList<string> TagsAdded { get; set; }

        /// <summary>
        /// Get or sets the list of removed tags.
        /// </summary>
        [JsonPropertyName("tags_removed")]
        public IReadOnlyList<string> TagsRemoved { get; set; }

        /// <summary>
        /// Priority system usage flag
        /// </summary>
        [JsonPropertyName("queueing")]
        public bool Queueing { get; set; }

        /// <summary>
        /// Gets or sets the state of the server.
        /// </summary>
        [JsonPropertyName("server_state")]
        public GlobalTransferExtendedInfo ServerState { get; set; }

#pragma warning disable 618
        [JsonPropertyName("categories")]
        [JsonInclude]
        [JsonConverter(typeof(CategoriesConverter))]
        private CategoriesJson CategoriesField
        {
            get => CategoriesChanged == null ? null : new CategoriesJson(CategoriesChanged, false);
            set
            {
                CategoriesChanged = value?.Changed;
                if (value?.Added == true)
                    CategoriesAdded = value.Changed.Keys.ToList();
            }
        }

        void IJsonOnDeserialized.OnDeserialized()
        {
            // full_update can come after categories
            if (FullUpdate && CategoriesChanged != null)
                CategoriesAdded ??= CategoriesChanged.Keys.ToList();
        }
#pragma warning restore 618

        // Added: the categories came as a list of names, which are all added ones
        private sealed record CategoriesJson(IReadOnlyDictionary<string, Category> Changed, bool Added);

        /// <summary>
        /// Reads the categories from a list of names before API v2.1.0
        /// </summary>
        private sealed class CategoriesConverter : JsonConverter<CategoriesJson>
        {
            public override CategoriesJson Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    var names = JsonSerializer.Deserialize<List<string>>(ref reader, options);
                    var categories = new Dictionary<string, Category>(names.Count);
                    foreach (var name in names)
                        categories[name] = new Category { Name = name, SavePath = string.Empty };
                    return new CategoriesJson(categories, true);
                }

                if (reader.TokenType == JsonTokenType.StartObject)
                    return new CategoriesJson(JsonSerializer.Deserialize<Dictionary<string, Category>>(ref reader, options), false);

                throw new JsonException($"Unexpected token {reader.TokenType}.");
            }

            public override void Write(Utf8JsonWriter writer, CategoriesJson value, JsonSerializerOptions options) =>
                JsonSerializer.Serialize(writer, value.Changed, options);
        }
    }
}
