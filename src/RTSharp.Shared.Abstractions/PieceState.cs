namespace RTSharp.Shared.Abstractions;

public enum PieceState : byte
{
    NotDownloaded,
    Downloading,
    Downloaded,

    /// <summary>
    /// Reserved
    /// </summary>
    Unknown
}
