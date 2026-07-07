namespace RTSharp.Shared.Abstractions;

public readonly record struct TorrentFiles(bool MultiFile, IList<File> Files);
