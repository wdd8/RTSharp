namespace RTSharp.Scripts.DuplicateTorrent;

public partial record DuplicateTorrentInput(byte[][] Hashes, string SourceProvider, string SourceUri, DuplicateTorrentTarget[] Targets, byte[]? TargetTorrent, bool Reflink, bool Hardlink);
public partial record DuplicateTorrentTarget(string Provider, string Uri, string Path, bool AddsV2OnlyTorrents);
public partial record DuplicateTorrentResult(byte[] AddedHash);
