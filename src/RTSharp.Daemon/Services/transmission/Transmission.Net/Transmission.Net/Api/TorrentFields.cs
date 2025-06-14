using Transmission.Net.Core.Entity;

namespace Transmission.Net.Api;

/// <summary>
/// Torrent fields
/// </summary>
public static class TorrentFields
{
    /// <inheritdoc cref="ITorrentView.ActivityDate"/>
    public const string ACTIVITY_DATE = "activityDate";

    /// <inheritdoc cref="ITorrentView.AddedDate"/>
    public const string ADDED_DATE = "addedDate";

    /// <inheritdoc cref="ITorrentData.BandwidthPriority"/>
    public const string BANDWIDTH_PRIORITY = "bandwidthPriority";

    /// <inheritdoc cref="ITorrentView.Comment"/>
    public const string COMMENT = "comment";

    /// <inheritdoc cref="ITorrentView.CorruptEver"/>
    public const string CORRUPT_EVER = "corruptEver";

    /// <inheritdoc cref="ITorrentView.Creator"/>
    public const string CREATOR = "creator";

    /// <inheritdoc cref="ITorrentView.DateCreated"/>
    public const string DATE_CREATED = "dateCreated";

    /// <inheritdoc cref="ITorrentView.DesiredAvailable"/>
    public const string DESIRED_AVAILABLE = "desiredAvailable";

    /// <inheritdoc cref="ITorrentView.DoneDate"/>
    public const string DONE_DATE = "doneDate";

    /// <inheritdoc cref="ITorrentView.DownloadDir"/>
    public const string DOWNLOAD_DIR = "downloadDir";

    /// <inheritdoc cref="ITorrentView.DownloadedEver"/>
    public const string DOWNLOADED_EVER = "downloadedEver";

    /// <inheritdoc cref="ITorrentData.DownloadLimit"/>
    public const string DOWNLOAD_LIMIT = "downloadLimit";

    /// <inheritdoc cref="ITorrentData.DownloadLimited"/>
    public const string DOWNLOAD_LIMITED = "downloadLimited";

    /// <inheritdoc cref="ITorrentView.EditDate"/>
    public const string EDIT_DATE = "editDate";

    /// <inheritdoc cref="ITorrentView.Error"/>
    public const string ERROR = "error";

    /// <inheritdoc cref="ITorrentView.ErrorString"/>
    public const string ERROR_STRING = "errorString";

    /// <inheritdoc cref="ITorrentView.Eta"/>
    public const string ETA = "eta";

    /// <inheritdoc cref="ITorrentView.EtaIdle"/>
    public const string ETA_IDLE = "etaIdle";

    /// <inheritdoc cref="ITorrentView.FileCount"/>
    public const string FILE_COUNT = "file-count";

    /// <inheritdoc cref="ITorrentView.Files"/>
    public const string FILES = "files";

    /// <inheritdoc cref="ITorrentView.FileStats"/>
    public const string FILE_STATS = "fileStats";

    /// <inheritdoc cref="ITorrentView.HashString"/>
    public const string HASH_STRING = "hashString";

    /// <inheritdoc cref="ITorrentView.HaveUnchecked"/>
    public const string HAVE_UNCHECKED = "haveUnchecked";

    /// <inheritdoc cref="ITorrentView.HaveValid"/>
    public const string HAVE_VALID = "haveValid";

    /// <inheritdoc cref="ITorrentData.HonorsSessionLimits"/>
    public const string HONORS_SESSION_LIMITS = "honorsSessionLimits";

    /// <inheritdoc cref="ITorrentView.Id"/>
    public const string ID = "id";

    /// <inheritdoc cref="ITorrentView.IsFinished"/>
    public const string IS_FINISHED = "isFinished";

    /// <inheritdoc cref="ITorrentView.IsPrivate"/>
    public const string IS_PRIVATE = "isPrivate";

    /// <inheritdoc cref="ITorrentView.IsStalled"/>
    public const string IS_STALLED = "isStalled";

    /// <inheritdoc cref="ITorrentData.Labels"/>
    public const string LABELS = "labels";

    /// <inheritdoc cref="ITorrentView.LeftUntilDone"/>
    public const string LEFT_UNTIL_DONE = "leftUntilDone";

    /// <inheritdoc cref="ITorrentView.MagnetLink"/>
    public const string MAGNET_LINK = "magnetLink";

    /// <inheritdoc cref="ITorrentView.ManualAnnounceTime"/>
    public const string MANUAL_ANNOUNCE_TIME = "manualAnnounceTime";

    /// <inheritdoc cref="ITorrentView.MaxConnectedPeers"/>
    public const string MAX_CONNECTED_PEERS = "maxConnectedPeers";

    /// <inheritdoc cref="ITorrentView.MetadataPercentComplete"/>
    public const string METADATA_PERCENT_COMPLETE = "metadataPercentComplete";

    /// <inheritdoc cref="ITorrentView.Name"/>
    public const string NAME = "name";

    /// <inheritdoc cref="ITorrentData.PeerLimit"/>
    public const string PEER_LIMIT = "peer-limit";

    /// <inheritdoc cref="ITorrentView.Peers"/>
    public const string PEERS = "peers";

    /// <inheritdoc cref="ITorrentView.PeersConnected"/>
    public const string PEERS_CONNECTED = "peersConnected";

    /// <inheritdoc cref="ITorrentView.PeersFrom"/>
    public const string PEERS_FROM = "peersFrom";

    /// <inheritdoc cref="ITorrentView.PeersGettingFromUs"/>
    public const string PEERS_GETTING_FROM_US = "peersGettingFromUs";

    /// <inheritdoc cref="ITorrentView.PeersSendingToUs"/>
    public const string PEERS_SENDING_TO_US = "peersSendingToUs";

    /// <inheritdoc cref="ITorrentView.PercentComplete"/>
    public const string PERCENT_COMPLETE = "percentComplete";

    /// <inheritdoc cref="ITorrentView.PercentDone"/>
    public const string PERCENT_DONE = "percentDone";

    /// <inheritdoc cref="ITorrentView.Pieces"/>
    public const string PIECES = "pieces";

    /// <inheritdoc cref="ITorrentView.PieceCount"/>
    public const string PIECE_COUNT = "pieceCount";

    /// <inheritdoc cref="ITorrentView.PieceSize"/>
    public const string PIECE_SIZE = "pieceSize";

    /// <inheritdoc cref="ITorrentView.Priorities"/>
    public const string PRIORITIES = "priorities";

    /// <inheritdoc cref="ITorrentView.PrimaryMimeType"/>
    public const string PRIMARY_MIME_TYPE = "primary-mime-type";

    /// <inheritdoc cref="ITorrentData.QueuePosition"/>
    public const string QUEUE_POSITION = "queuePosition";

    /// <inheritdoc cref="ITorrentView.RateDownload"/>
    public const string RATE_DOWNLOAD = "rateDownload";

    /// <inheritdoc cref="ITorrentView.RateUpload"/>
    public const string RATE_UPLOAD = "rateUpload";

    /// <inheritdoc cref="ITorrentView.RecheckProgress"/>
    public const string RECHECK_PROGRESS = "recheckProgress";

    /// <inheritdoc cref="ITorrentView.SecondsDownloading"/>
    public const string SECONDS_DOWNLOADING = "secondsDownloading";

    /// <inheritdoc cref="ITorrentView.SecondsSeeding"/>
    public const string SECONDS_SEEDING = "secondsSeeding";

    /// <inheritdoc cref="ITorrentData.SeedIdleLimit"/>
    public const string SEED_IDLE_LIMIT = "seedIdleLimit";

    /// <inheritdoc cref="ITorrentData.SeedIdleMode"/>
    public const string SEED_IDLE_MODE = "seedIdleMode";

    /// <inheritdoc cref="ITorrentData.SeedRatioLimit"/>
    public const string SEED_RATIO_LIMIT = "seedRatioLimit";

    /// <inheritdoc cref="ITorrentData.SeedRatioMode"/>
    public const string SEED_RATIO_MODE = "seedRatioMode";

    /// <inheritdoc cref="ITorrentView.SizeWhenDone"/>
    public const string SIZE_WHEN_DONE = "sizeWhenDone";

    /// <inheritdoc cref="ITorrentView.StartDate"/>
    public const string START_DATE = "startDate";

    /// <inheritdoc cref="ITorrentView.Status"/>
    public const string STATUS = "status";

    /// <inheritdoc cref="ITorrentView.Trackers"/>
    public const string TRACKERS = "trackers";

    /// <inheritdoc cref="ITorrentData.TrackerList"/>
    public const string TRACKER_LIST = "trackerList";

    /// <inheritdoc cref="ITorrentView.TrackerStats"/>
    public const string TRACKER_STATS = "trackerStats";

    /// <inheritdoc cref="ITorrentView.TotalSize"/>
    public const string TOTAL_SIZE = "totalSize";

    /// <inheritdoc cref="ITorrentView.TorrentFile"/>
    public const string TORRENT_FILE = "torrentFile";

    /// <inheritdoc cref="ITorrentView.UploadedEver"/>
    public const string UPLOADED_EVER = "uploadedEver";

    /// <inheritdoc cref="ITorrentData.UploadLimit"/>
    public const string UPLOAD_LIMIT = "uploadLimit";

    /// <inheritdoc cref="ITorrentData.UploadLimited"/>
    public const string UPLOAD_LIMITED = "uploadLimited";

    /// <inheritdoc cref="ITorrentView.UploadRatio"/>
    public const string UPLOAD_RATIO = "uploadRatio";

    /// <inheritdoc cref="ITorrentView.Wanted"/>
    public const string WANTED = "wanted";

    /// <inheritdoc cref="ITorrentView.Webseeds"/>
    public const string WEBSEEDS = "webseeds";

    /// <inheritdoc cref="ITorrentView.WebseedsSendingToUs"/>
    public const string WEBSEEDS_SENDING_TO_US = "webseedsSendingToUs";

    /// <summary>
    /// All fields
    /// </summary>
    public static string[] ALL_FIELDS => new string[]
            {
                #region ALL FIELDS
                ACTIVITY_DATE,
                ADDED_DATE,
                BANDWIDTH_PRIORITY,
                COMMENT,
                CORRUPT_EVER,
                CREATOR,
                DATE_CREATED,
                DESIRED_AVAILABLE,
                DONE_DATE,
                DOWNLOAD_DIR,
                DOWNLOADED_EVER,
                DOWNLOAD_LIMIT,
                DOWNLOAD_LIMITED,
                EDIT_DATE,
                ERROR,
                ERROR_STRING,
                ETA,
                ETA_IDLE,
                FILE_COUNT,
                FILES,
                FILE_STATS,
                HASH_STRING,
                HAVE_UNCHECKED,
                HAVE_VALID,
                HONORS_SESSION_LIMITS,
                ID,
                IS_FINISHED,
                IS_PRIVATE,
                IS_STALLED,
                LABELS,
                LEFT_UNTIL_DONE,
                MAGNET_LINK,
                MANUAL_ANNOUNCE_TIME,
                MAX_CONNECTED_PEERS,
                METADATA_PERCENT_COMPLETE,
                NAME,
                PEER_LIMIT,
                PEERS,
                PEERS_CONNECTED,
                PEERS_FROM,
                PEERS_GETTING_FROM_US,
                PEERS_SENDING_TO_US,
                PERCENT_COMPLETE,
                PERCENT_DONE,
                PIECES,
                PIECE_COUNT,
                PIECE_SIZE,
                PRIORITIES,
                PRIMARY_MIME_TYPE,
                QUEUE_POSITION,
                RATE_DOWNLOAD,
                RATE_UPLOAD,
                RECHECK_PROGRESS,
                SECONDS_DOWNLOADING,
                SECONDS_SEEDING,
                SEED_IDLE_LIMIT,
                SEED_IDLE_MODE,
                SEED_RATIO_LIMIT,
                SEED_RATIO_MODE,
                SIZE_WHEN_DONE,
                START_DATE,
                STATUS,
                TRACKERS,
                TRACKER_LIST,
                TRACKER_STATS,
                TOTAL_SIZE,
                TORRENT_FILE,
                UPLOADED_EVER,
                UPLOAD_LIMIT,
                UPLOAD_LIMITED,
                UPLOAD_RATIO,
                WANTED,
                WEBSEEDS,
                WEBSEEDS_SENDING_TO_US,
                #endregion
            };
}
