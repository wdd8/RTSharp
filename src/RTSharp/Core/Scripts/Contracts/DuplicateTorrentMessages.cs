namespace RTSharp.Scripts.DuplicateTorrent;

public partial record DuplicateTorrentInput(byte[] Hash, string SourceProvider, string SourceUri, DuplicateTorrentTarget[] Targets);
public partial record DuplicateTorrentTarget(string Provider, string Uri, string Path);
