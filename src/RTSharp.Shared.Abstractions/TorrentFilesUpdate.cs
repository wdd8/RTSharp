namespace RTSharp.Shared.Abstractions;

/// <summary>
/// A single message on a torrent file update stream.
/// Either <paramref name="FullUpdate"/> or <paramref name="Changes"/> will be
/// non-null, but not both.
/// </summary>
public record TorrentFilesUpdate(TorrentFiles? FullUpdate, IList<FileDelta> Changes);

public record FileDelta(int Index, ulong DownloadedPieces, ulong Downloaded, File.PRIORITY Priority, File.DOWNLOAD_STRATEGY DownloadStrategy);
