namespace RTSharp.Scripts.CrossSeedSearch;

public partial record CrossSeedIndexer(string Name, string Url);

public partial record CrossSeedSource(byte[] Torrent, string BaseDirectory);

public partial record CrossSeedSearchInput(
    CrossSeedSource[] Sources,
    CrossSeedIndexer[] Indexers,
    string[] ExistingInfoHashes
);

public partial record CrossSeedMatch(string Indexer, string Title, byte[] Torrent, bool Linked);
public partial record CrossSeedSkip(string Indexer, string Title, string Reason);
public partial record CrossSeedSearchResult(CrossSeedMatch[] Matches, CrossSeedSkip[] Skipped);
