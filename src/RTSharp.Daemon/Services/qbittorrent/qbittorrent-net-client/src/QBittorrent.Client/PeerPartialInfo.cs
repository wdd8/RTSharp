using System.Collections.Generic;
using System.Net;
using System.Text.Json.Serialization;
using QBittorrent.Client.Converters;

namespace QBittorrent.Client
{
    /// <summary>
    /// Provides the full or partial information about a torrent peer.
    /// </summary>
    public class PeerPartialInfo
    {
        /// <summary>
        /// Gets or sets the peer IP address.
        /// </summary>
        /// <value>
        /// The peer IP address.
        /// </value>
        [JsonPropertyName("ip")]
        [JsonConverter(typeof(StringToIpAddressConverter))]
        public IPAddress Address { get; set; }

        /// <summary>
        /// Gets or sets the port.
        /// </summary>
        /// <value>
        /// The port.
        /// </value>
        [JsonPropertyName("port")]
        public int? Port { get; set; }

        /// <summary>
        /// Gets or sets the peer client.
        /// </summary>
        /// <value>
        /// The client.
        /// </value>
        [JsonPropertyName("client")]
        public string Client { get; set; }

        /// <summary>
        /// Gets or sets the progress.
        /// </summary>
        /// <value>
        /// The progress.
        /// </value>
        [JsonPropertyName("progress")]
        public double? Progress { get; set; }

        /// <summary>
        /// Gets or sets the download speed.
        /// </summary>
        /// <value>
        /// The download speed.
        /// </value>
        [JsonPropertyName("dl_speed")]
        public int? DownloadSpeed { get; set; }

        /// <summary>
        /// Gets or sets the upload speed.
        /// </summary>
        /// <value>
        /// The upload speed.
        /// </value>
        [JsonPropertyName("up_speed")]
        public int? UploadSpeed { get; set; }

        /// <summary>
        /// Gets or sets the amount of downloaded data.
        /// </summary>
        /// <value>
        /// The amount of downloaded data.
        /// </value>
        [JsonPropertyName("downloaded")]
        public long? Downloaded { get; set; }

        /// <summary>
        /// Gets or sets the amount of uploaded data.
        /// </summary>
        /// <value>
        /// The amount of uploaded data.
        /// </value>
        [JsonPropertyName("uploaded")]
        public long? Uploaded { get; set; }

        /// <summary>
        /// Gets or sets the type of the connection.
        /// </summary>
        /// <value>
        /// The type of the connection.
        /// </value>
        [JsonPropertyName("connection")]
        public string ConnectionType { get; set; }

        /// <summary>
        /// Gets or sets the flags.
        /// </summary>
        /// <value>
        /// The flags.
        /// </value>
        [JsonPropertyName("flags")]
        public string Flags { get; set; }

        /// <summary>
        /// Gets or sets the flags description.
        /// </summary>
        /// <value>
        /// The flags description.
        /// </value>
        [JsonPropertyName("flags_desc")]
        public string FlagsDescription { get; set; }

        /// <summary>
        /// Gets or sets the relevance.
        /// </summary>
        /// <value>
        /// The relevance.
        /// </value>
        [JsonPropertyName("relevance")]
        public double? Relevance { get; set; }

        /// <summary>
        /// Gets or sets the files.
        /// </summary>
        /// <value>
        /// The files.
        /// </value>
        [JsonPropertyName("files")]
        [StringToList("\n", false)]
        public IReadOnlyList<string> Files { get; set; }

        /// <summary>
        /// Gets or sets the country.
        /// </summary>
        /// <value>
        /// The country.
        /// </value>
        [JsonPropertyName("country")]
        public string Country { get; set; }

        /// <summary>
        /// Gets or sets the country code.
        /// </summary>
        /// <value>
        /// The country code.
        /// </value>
        [JsonPropertyName("country_code")]
        public string CountryCode { get; set; }    }
}
