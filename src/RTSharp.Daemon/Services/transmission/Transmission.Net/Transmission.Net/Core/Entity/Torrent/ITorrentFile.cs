using Newtonsoft.Json;

namespace Transmission.Net.Core.Entity.Torrent;

public interface ITorrentFile
{
    /// <summary>
    /// The current size of the file, i.e. how much we've downloaded
    /// </summary>
    [JsonProperty("bytesCompleted")]
    long? BytesCompleted { get; set; }

    /// <summary>
    /// The total size of the file
    /// </summary>
    [JsonProperty("length")]
    long? Length { get; set; }

    /// <summary>
    /// This file's name. Includes the full subpath in the torrent.
    /// </summary>
    [JsonProperty("name")]
    string? Name { get; set; }

    /// <summary>
    /// Starting piece index
    /// </summary>
    [JsonProperty("begin_piece")]
    long? BeginPiece { get; set; }

    /// <summary>
    /// Ending piece index
    /// </summary>
    [JsonProperty("end_piece")]
    long? EndPiece { get; set; }
}
