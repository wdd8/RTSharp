using System.Text.Json.Serialization;

namespace QBittorrent.Client
{
    /// <summary>
    /// Describes qBittorrent build information.
    /// </summary>
    public class BuildInfo
    {
        /// <summary>
        /// The version of Qt.
        /// </summary>
        [JsonPropertyName("qt")]
        public string QtVersion { get; set; }
        
        /// <summary>
        /// The version of libtorrent.
        /// </summary>
        [JsonPropertyName("libtorrent")]
        public string LibtorrentVersion { get; set; }

        /// <summary>
        /// The version of Boost.
        /// </summary>
        [JsonPropertyName("boost")]
        public string BoostVersion { get; set; }

        /// <summary>
        /// The version of OpenSSL.
        /// </summary>
        [JsonPropertyName("openssl")]
        public string OpenSslVersion { get; set; }

        /// <summary>
        /// The version of zlib.
        /// </summary>
        [JsonPropertyName("zlib")]
        public string ZlibVersion { get; set; }

        /// <summary>
        /// QBittorrent bitness.
        /// </summary>
        [JsonPropertyName("bitness")]
        public int Bitness { get; set; }

        /// <summary>
        /// The platform qBittorrent is running on.
        /// </summary>
        [JsonPropertyName("platform")]
        public string Platform { get; set; }    }
}
