using System.Text.Json.Serialization;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represent information about a network interface.
    /// </summary>
    public class NetInterface
    {
        /// <summary>
        /// Gets or sets the internal network interface identifier.
        /// </summary>
        [JsonPropertyName("value")]
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the network interface name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }    }
}
