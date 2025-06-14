using Newtonsoft.Json;
using Transmission.Net.Api.Entity.Torrent;
using Transmission.Net.Core.Entity;
using Transmission.Net.Core.Entity.Torrent;
using Transmission.Net.Core.Enums;

namespace Transmission.Net.Api.Entity;

/// <summary>
/// Torrent information
/// </summary>
public class TorrentView : ITorrentView
{
    public TorrentView() { }

    [JsonConstructor]
    internal TorrentView(
        TorrentFile[]? files,
        TorrentFileStats[]? fileStats,
        TorrentPeers[]? peers,
        TorrentPeersFrom? peersFrom,
        TorrentTracker[]? trackers,
        TorrentTrackerStats[]? trackerStats)
    {
        Files = files;
        FileStats = fileStats;
        Peers = peers;
        PeersFrom = peersFrom;
        Trackers = trackers;
        TrackerStats = trackerStats;
    }

    public int? Id { get; set; }
    public DateTime? ActivityDate { get; set; }
    public DateTime? AddedDate { get; set; }
    public Priority? BandwidthPriority { get; set; }
    public string? Comment { get; set; }
    public int? CorruptEver { get; set; }
    public string? Creator { get; set; }
    public DateTime? DateCreated { get; set; }
    public long? DesiredAvailable { get; set; }
    public DateTime? DoneDate { get; set; }
    public string? DownloadDir { get; set; }
    public long? DownloadedEver { get; set; }
    public int? DownloadLimit { get; set; }
    public bool? DownloadLimited { get; set; }
    public DateTime? EditDate { get; set; }
    public TorrentError? Error { get; set; }
    public string? ErrorString { get; set; }
    public int? Eta { get; set; }
    public int? EtaIdle { get; set; }
    public int? FileCount { get; set; }
    public ITorrentFile[]? Files { get; set; }
    public ITorrentFileStats[]? FileStats { get; set; }
    public string? HashString { get; set; }
    public long? HaveUnchecked { get; set; }
    public long? HaveValid { get; set; }
    public long? Have => HaveUnchecked + HaveValid;
    public bool? HonorsSessionLimits { get; set; }
    public bool? IsFinished { get; set; }
    public bool? IsPrivate { get; set; }
    public bool? IsStalled { get; set; }
    public string[]? Labels { get; set; }
    public long? LeftUntilDone { get; set; }
    public string? MagnetLink { get; set; }
    public int? ManualAnnounceTime { get; set; }
    public int? MaxConnectedPeers { get; set; }
    public double? MetadataPercentComplete { get; set; }
    public string? Name { get; set; }
    public int? PeerLimit { get; set; }
    public ITorrentPeers[]? Peers { get; set; }
    public int? PeersConnected { get; set; }
    public ITorrentPeersFrom? PeersFrom { get; set; }
    public int? PeersGettingFromUs { get; set; }
    public int? PeersSendingToUs { get; set; }
    public double? PercentComplete { get; set; }
    public double? PercentDone { get; set; }
    public string? Pieces { get; set; }
    public int? PieceCount { get; set; }
    public long? PieceSize { get; set; }
    public Priority[]? Priorities { get; set; }
    public string? PrimaryMimeType { get; set; }
    public int? QueuePosition { get; set; }
    public int? RateDownload { get; set; }
    public int? RateUpload { get; set; }
    public double? RecheckProgress { get; set; }
    public int? SecondsDownloading { get; set; }
    public int? SecondsSeeding { get; set; }
    public int? SeedIdleLimit { get; set; }
    public int? SeedIdleMode { get; set; }
    public double? SeedRatioLimit { get; set; }
    public int? SeedRatioMode { get; set; }
    public long? SizeWhenDone { get; set; }
    public DateTime? StartDate { get; set; }
    public TorrentStatus? Status { get; set; }
    public ITorrentTracker[]? Trackers { get; set; }
    public string? TrackerList { get; set; }
    public ITorrentTrackerStats[]? TrackerStats { get; set; }
    public long? TotalSize { get; set; }
    public string? TorrentFile { get; set; }
    public long? UploadedEver { get; set; }
    public int? UploadLimit { get; set; }
    public bool? UploadLimited { get; set; }
    public double? UploadRatio { get; set; }
    public bool[]? Wanted { get; set; }
    public string[]? Webseeds { get; set; }
    public int? WebseedsSendingToUs { get; set; }
}
