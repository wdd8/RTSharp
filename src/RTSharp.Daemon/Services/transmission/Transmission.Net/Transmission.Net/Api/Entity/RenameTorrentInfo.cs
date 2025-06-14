using Newtonsoft.Json;

namespace Transmission.Net.Api.Entity;

/// <summary>
/// Rename torrent result information
/// </summary>
public class RenameTorrentInfo
{
    public RenameTorrentInfo(int id, string path, string name)
    {
        Id = id;
        Path = path;
        Name = name;
    }

    /// <summary>
    /// The torrent's unique Id.
    /// </summary>
    [JsonProperty("id")]
    public int Id { get; set; }

    /// <summary>
    /// File path.
    /// </summary>
    [JsonProperty("path")]
    public string Path { get; set; }

    /// <summary>
    /// File name.
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; }
}
