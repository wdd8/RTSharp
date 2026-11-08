using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using QBittorrent.Client.Converters;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents QBittorrent peer log entry.
    /// </summary>
    public class PeerLogEntry
    {
        /// <summary>
        /// Gets or sets the ID of the peer.
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the IP address of the peer.
        /// </summary>
        [JsonPropertyName("ip")]
        [JsonConverter(typeof(StringToIpAddressConverter))]
        public IPAddress Address { get; set; }

        /// <summary>
        /// Gets or sets the timestamp (milliseconds since epoch).
        /// </summary>
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the peer was blocked.
        /// </summary>
        [JsonPropertyName("blocked")]
        public bool Blocked { get; set; }

        /// <summary>
        /// Gets or sets the reason of the block.
        /// </summary>
        [JsonPropertyName("reason")]
        public string Reason { get; set; }
    }
}
