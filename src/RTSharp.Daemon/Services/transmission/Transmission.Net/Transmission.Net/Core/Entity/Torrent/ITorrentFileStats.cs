using Newtonsoft.Json;
using Transmission.Net.Core.Enums;

namespace Transmission.Net.Core.Entity.Torrent;

public interface ITorrentFileStats
{
    /// <summary>
    /// <inheritdoc cref="ITorrentFile.BytesCompleted"/>
    /// </summary>
    [JsonProperty("bytesCompleted")]
    double BytesCompleted { get; set; }

    /// <summary>
    /// Do we want this file?
    /// </summary>
    [JsonProperty("wanted")]
    bool Wanted { get; set; }

    /// <summary>
    /// The file's priority
    /// </summary>
    [JsonProperty("priority")]
    Priority Priority { get; set; }
}
