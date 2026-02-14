namespace RTSharp.Shared.Abstractions;

public enum PieceState : byte
{
    NotDownloaded,
    Downloading,
    Downloaded,
    Highlighted,

    /// <summary>
    /// Reserved
    /// </summary>
    Unknown
}
