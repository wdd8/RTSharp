namespace Transmission.Net.Core.Enums;

/// <summary>
/// Announce or scrape state of a tracker
/// </summary>
public enum TrackerState
{
    /// <summary>
    /// We won't announce/scrape this torrent to this tracker because
    /// the torrent is stopped, or because of an error, or whatever
    /// </summary>
    Inactive = 0,
    /// <summary>
    /// We will announce/scrape this torrent to this tracker, and are
    /// waiting for enough time to pass to satisfy the tracker's interval
    /// </summary>
    Waiting = 1,
    /// <summary>
    /// It's time to announce/scrape this torrent, and we're waiting on a
    /// a free slot to open up in the announce manager
    /// </summary>
    Queued = 2,
    /// <summary>
    /// We're announcing/scraping this torrent right now
    /// </summary>
    Active = 3
};
