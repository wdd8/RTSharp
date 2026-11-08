using System.Text.Json.Serialization;

namespace QBittorrent.Client
{
    /// <summary>
    /// Describes torrent category and the corresponding save path.
    /// </summary>
    public class Category
    {
        /// <summary>
        /// Category name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Category save path.
        /// </summary>
        [JsonPropertyName("savePath")]
        public string SavePath { get; set; }

        // Older versions name the save path "save_path"
        [JsonPropertyName("save_path")]
        [JsonInclude]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        private string LegacySavePath
        {
            get => null;
            set
            {
                if (value != null)
                    SavePath = value;
            }
        }
    }
}
