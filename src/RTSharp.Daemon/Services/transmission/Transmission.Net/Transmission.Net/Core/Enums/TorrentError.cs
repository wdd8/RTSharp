namespace Transmission.Net.Core.Enums;

public enum TorrentError
{
    /// <summary>
    /// Everything's fine
    /// </summary>
    Ok = 0,
    /// <summary>
    /// When we anounced to the tracker, we got a warning in the response
    /// </summary>
    TrackerWarning = 1,
    /// <summary>
    /// When we anounced to the tracker, we got an error in the response
    /// </summary>
    TrackerError = 2,
    /// <summary>
    /// Local trouble, such as disk full or permissions error
    /// </summary>
    LocalError = 3
}
