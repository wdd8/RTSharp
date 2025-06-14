namespace Transmission.Net.Core.Enums;

public enum TorrentStatus
{
    /// <summary>
    /// Torrent is stopped
    /// </summary>
    Stopped = 0,

    /// <summary>
    /// Torrent is queued to verify local data
    /// </summary>
    VerifyQueue = 1,

    /// <summary>
    /// Torrent is verifying local data
    /// </summary>
    Verifying = 2,

    /// <summary>
    /// Torrent is queued to download
    /// </summary>
    DownloadQueue = 3,

    /// <summary>
    /// Torrent is downloading
    /// </summary>
    Downloading = 4,

    /// <summary>
    /// Torrent is queued to seed
    /// </summary>
    QueueSeed = 5,

    /// <summary>
    /// Torrent is seeding
    /// </summary>
    Seeding = 6,
}
