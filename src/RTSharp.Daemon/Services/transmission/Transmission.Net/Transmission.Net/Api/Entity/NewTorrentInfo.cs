using Newtonsoft.Json;

namespace Transmission.Net.Api.Entity;

/// <summary>
/// Information of added torrent
/// </summary>
public class NewTorrentInfo
{
    public NewTorrentInfo(int id, string name, string hashString)
    {
        Id = id;
        Name = name;
        HashString = hashString;
    }

    /// <summary>
    /// Torrent ID
    /// </summary>
    [JsonProperty("id")]
    public int Id { get; set; }

    /// <summary>
    /// Torrent name
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; }

    /// <summary>
    /// Torrent Hash
    /// </summary>
    [JsonProperty("hashString")]
    public string HashString { get; set; }

}
