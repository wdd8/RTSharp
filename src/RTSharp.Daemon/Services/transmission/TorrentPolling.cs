using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Services.TorrentPoll;
using RTSharp.Daemon.Exceptions;
using RTSharp.Daemon.Utils;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;
using System.Collections.Concurrent;
using Transmission.Net.Api;

namespace RTSharp.Daemon.Services.transmission;

public sealed class TorrentPolling([ServiceKey] string InstanceKey, IServiceProvider Services, ILogger<TorrentPolling> Logger, EventsService Events) : TorrentPollingService(Logger, Events)
{
    private bool PollInitialized;
    private DateTime LastSuccessfulPoll;
    private readonly ConcurrentInfoHashDictionary<Protocols.DataProvider.Torrent> LatestTorrents = new();
    private readonly ConcurrentInfoHashDictionary<int> HashToId = new();
    private readonly ConcurrentDictionary<int, byte[]> IdToHash = new();
    private readonly ConcurrentQueue<byte[]> FullUpdateQueue = new();
    private static readonly string[] AllFieldsExceptLists = TorrentFields.ALL_FIELDS.Except([
        TorrentFields.FILES, TorrentFields.FILE_STATS, TorrentFields.PEERS,
        TorrentFields.TRACKER_STATS, TorrentFields.TRACKER_LIST, TorrentFields.PIECES
    ]).ToArray();

    internal void RequestFullUpdate(ReadOnlySpan<byte> Hash) => FullUpdateQueue.Enqueue(Hash.ToArray());

    protected internal override DeltaTorrentsListResponse BuildChanges(HistoryEntry? Previous, HistoryEntry Current)
    {
        var delta = new DeltaTorrentsListResponse();
        foreach (var (hash, torrent) in Current.List) {
            if (Previous == null || !Previous.List.TryGetValue(hash, out var old)) {
                delta.FullUpdate.Add(torrent.Clone());
                continue;
            }

            if (torrent.Name != old.Name ||
                torrent.Size != old.Size ||
                torrent.IsCompleted != old.IsCompleted ||
                torrent.IsPrivate != old.IsPrivate ||
                torrent.ChunkSize != old.ChunkSize ||
                torrent.State != old.State ||
                torrent.WantedSize != old.WantedSize ||
                torrent.Priority != old.Priority ||
                torrent.MagnetDummy != old.MagnetDummy ||
                torrent.SequentialDownload != old.SequentialDownload ||
                torrent.RemotePath != old.RemotePath ||
                torrent.StatusMessage != old.StatusMessage ||
                !Equals(torrent.FinishedOn, old.FinishedOn) ||
                !torrent.Labels.SequenceEqual(old.Labels) ||
                torrent.PrimaryTracker?.Uri != old.PrimaryTracker?.Uri) {
                delta.FullUpdate.Add(torrent.Clone());
            } else if (torrent.IsCompleted) {
                if (torrent.Uploaded != old.Uploaded ||
                    torrent.UPSpeed != old.UPSpeed ||
                    torrent.SeedersTotal != old.SeedersTotal ||
                    torrent.PeersTotal != old.PeersTotal ||
                    torrent.PeersConnected != old.PeersConnected)
                    delta.Complete.Add(TorrentDeltaMessages.Complete(torrent));
            } else if (torrent.Uploaded != old.Uploaded ||
                torrent.Downloaded != old.Downloaded ||
                torrent.CompletedSize != old.CompletedSize ||
                torrent.DLSpeed != old.DLSpeed ||
                torrent.HashingSpeed != old.HashingSpeed ||
                torrent.UPSpeed != old.UPSpeed ||
                torrent.SeedersConnected != old.SeedersConnected ||
                torrent.SeedersTotal != old.SeedersTotal ||
                torrent.PeersConnected != old.PeersConnected ||
                torrent.PeersTotal != old.PeersTotal ||
                torrent.Wasted != old.Wasted ||
                !Equals(torrent.ETA, old.ETA)) {
                delta.Incomplete.Add(TorrentDeltaMessages.Incomplete(torrent));
            }
        }

        if (Previous != null) {
            foreach (var (hash, torrent) in Previous.List) {
                if (!Current.List.ContainsKey(hash))
                    delta.Removed.Add(torrent.Hash);
            }
        }
        return delta;
    }

    protected override async Task<HistoryEntry> PollAsync(CancellationToken CancellationToken)
    {
        CancellationToken.ThrowIfCancellationRequested();
        var main = Services.GetRequiredKeyedService<Grpc>(InstanceKey);
        var backend = Services.GetRequiredKeyedService<TransmissionClient>(InstanceKey);
        try {
            var client = await backend.Init();
            // Transmission's recently-active window is a fixed 60s regardless of our poll interval (30s slack)
            var all = !PollInitialized || DateTime.UtcNow - LastSuccessfulPoll > TimeSpan.FromSeconds(30);
            var refresh = new List<string>();
            while (FullUpdateQueue.TryDequeue(out var hash))
                refresh.Add(main.ToNativeHashString(hash));
            var result = all ? await client.TorrentGetAsync(null, AllFieldsExceptLists) : await client.TorrentGetRecentyActiveAsync(AllFieldsExceptLists);

            if (!all && refresh.Count > 0) {
                var extra = await client.TorrentGetAsync(refresh.Distinct().ToArray(), AllFieldsExceptLists);
                result!.Torrents = [.. result.Torrents, .. extra!.Torrents.ExceptBy(result.Torrents.Select(x => x.Id), x => x.Id)];
                if (extra.Removed != null)
                    result.Removed = [.. result.Removed ?? [], .. extra.Removed];
            }

            if (all) {
                LatestTorrents.Clear();
                HashToId.Clear();
                IdToHash.Clear();
            }
            foreach (var external in result!.Torrents) {
                var hash = await main.ResolveRTSHash(external);
                var id = external.Id!.Value;
                if (IdToHash.TryGetValue(id, out var oldHash) && !oldHash.AsSpan().SequenceEqual(hash)) {
                    LatestTorrents.TryRemove(oldHash, out _);
                    HashToId.TryRemove(oldHash, out _);
                }
                var torrent = LatestTorrents.TryGetValue(hash, out var previous) ?
                    previous.Clone() :
                    new Protocols.DataProvider.Torrent {
                        Hash = hash.ToByteString()
                    };
                TorrentMapper.ApplyFromExternal(torrent, external);
                UpdateHashingSpeed(torrent);
                LatestTorrents[hash] = torrent;
                HashToId[hash] = id;
                IdToHash[id] = hash;
            }
            if (result.Removed != null) {
                foreach (var id in result.Removed) {
                    if (!IdToHash.TryRemove(id, out var hash))
                        continue;
                    // The same hash can be removed and re-added with a new id
                    if (HashToId.TryGetValue(hash, out var currentId) && currentId == id) {
                        LatestTorrents.TryRemove(hash, out _);
                        HashToId.TryRemove(hash, out _);
                    }
                }
            }
            LastSuccessfulPoll = DateTime.UtcNow;
            PollInitialized = true;
            return new HistoryEntry(DateTime.UtcNow, LatestTorrents.ToInfoHashDictionary(x => x.Key, x => x.Value.Clone()));
        } catch (Exception ex) {
            // Recover the complete list after connection failures or a daemon restart
            PollInitialized = false;
            if (ex.IsConnectionFailure())
                throw new DataProviderFailure(ex.Message, ex, backend.Uri);
            throw;
        }
    }

    private void UpdateHashingSpeed(Protocols.DataProvider.Torrent Torrent)
    {
        if (Torrent.State.HasFlag(Protocols.DataProvider.TorrentState.Hashing)) {
            var sma = Services.GetRequiredService<ISpeedMovingAverageService>().Get(InstanceKey, $"HashingSpeed_{Convert.ToHexString(Torrent.Hash.Span)}");
            sma.ValueIfChanged(DateTime.UtcNow, (long)Torrent.CompletedSize);
            var hashingSpeed = (ulong)Math.Max(0, sma.CalculateSpeed());
            Torrent.HashingSpeed = hashingSpeed;

            // No native ETA for hashing state
            if (hashingSpeed == 0 || Torrent.IsCompleted) {
                Torrent.ETA = null;
            } else {
                var secs = (double)(Torrent.WantedSize - Torrent.CompletedSize) / hashingSpeed;
                Torrent.ETA = secs >= Duration.MaxSeconds ? null : Duration.FromTimeSpan(TimeSpan.FromSeconds(secs));
            }
        } else {
            Torrent.HashingSpeed = 0;
        }
    }
}
