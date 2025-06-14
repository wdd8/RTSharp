using Transmission.Net.Api;
using Transmission.Net.Core.Entity;
using Transmission.Net.Core.Enums;

namespace Transmission.Net.Arguments;

/// <summary>
/// Torrent settings
/// </summary>
public class TorrentSettings : ArgumentsBase, ITorrentData
{
    public Priority? BandwidthPriority { get => GetValue<Priority?>("bandwidthPriority"); set => this["bandwidthPriority"] = value; }
    public int? DownloadLimit { get => GetValue<int?>("downloadLimit"); set => this["downloadLimit"] = value; }
    public bool? DownloadLimited { get => GetValue<bool?>("downloadLimited"); set => this["downloadLimited"] = value; }
    public bool? HonorsSessionLimits { get => GetValue<bool?>("honorsSessionLimits"); set => this["honorsSessionLimits"] = value; }
    public int? PeerLimit { get => GetValue<int?>("peer-limit"); set => this["peer-limit"] = value; }

    /// <summary>
    /// Torrent id array, which specifies which torrents to use. All torrents are used if the ids argument is omitted.
    /// </summary>
    public object? IDs { get => GetValue<object?>("ids"); set => this["ids"] = value; }


    /// <summary>
    /// Indices of file(s) to not download
    /// </summary>
    public string? FilesUnwanted { get => GetValue<string?>("files-unwanted"); set => this["files-unwanted"] = value; }

    /// <summary>
    /// Indices of file(s) to download
    /// </summary>
    public string? FilesWanted { get => GetValue<string?>("files-wanted"); set => this["files-wanted"] = value; }

    /// <summary>
    /// New location of the torrent's content
    /// </summary>
    public string? Location { get => GetValue<string?>("location"); set => this["location"] = value; }

    /// <summary>
    /// Indices of high-priority file(s)
    /// </summary>
    public string? PriorityHigh { get => GetValue<string?>("priority-high"); set => this["priority-high"] = value; }

    /// <summary>
    /// Indices of low-priority file(s)
    /// </summary>
    public string? PriorityLow { get => GetValue<string?>("priority-low"); set => this["priority-low"] = value; }

    /// <summary>
    /// Indices of normal-priority file(s)
    /// </summary>
    public string? PriorityNormal { get => GetValue<string?>("priority-normal"); set => this["priority-normal"] = value; }
    public int? QueuePosition { get => GetValue<int?>("queuePosition"); set => this["queuePosition"] = value; }
    public int? SeedIdleLimit { get => GetValue<int?>("seedIdleLimit"); set => this["seedIdleLimit"] = value; }
    public int? SeedIdleMode { get => GetValue<int?>("seedIdleMode"); set => this["seedIdleMode"] = value; }
    public double? SeedRatioLimit { get => GetValue<double?>("seedRatioLimit"); set => this["seedRatioLimit"] = value; }
    public int? SeedRatioMode { get => GetValue<int?>("seedRatioMode"); set => this["seedRatioMode"] = value; }
    public int? UploadLimit { get => GetValue<int?>("uploadLimit"); set => this["uploadLimit"] = value; }
    public bool? UploadLimited { get => GetValue<bool?>("uploadLimited"); set => this["uploadLimited"] = value; }

    /// <summary>
    /// Strings of announce URLs to add
    /// </summary>
    [Obsolete("TrackerAdd is obsolete since Transmission 4.0.0, use TrackerList instead.")]
    public string? TrackerAdd { get => GetValue<string?>("trackerAdd"); set => this["trackerAdd"] = value; }

    /// <summary>
    /// Ids of trackers to remove
    /// </summary>
    [Obsolete("TrackerRemove is obsolete since Transmission 4.0.0, use TrackerList instead.")]
    public int[]? TrackerRemove { get => GetValue<int[]?>("trackerRemove"); set => this["trackerRemove"] = value; }

    //TODO: Add and test
    //"trackerReplace"      | array      pairs of <trackerId/new announce URLs>
    //public [] trackerReplace;

    public string? TrackerList { get => GetValue<string?>("trackerList"); set => this["trackerList"] = value; }
    public string[]? Labels { get => GetValue<string[]?>("labels"); set => this["labels"] = value; }
}
