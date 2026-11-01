using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Services.TorrentPoll;
using RTSharp.Daemon.Exceptions;
using RTSharp.Daemon.Utils;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;
using QBittorrent.Client;

namespace RTSharp.Daemon.Services.qbittorrent;

public sealed class TorrentPolling([ServiceKey] string InstanceKey, IServiceProvider Services, ILogger<TorrentPolling> Logger, EventsService Events) : TorrentPollingService(Logger, Events)
{
    private int PollResponseId;
    private InfoHashDictionary<Protocols.DataProvider.Torrent> LatestTorrents = new();
    private readonly ReaderWriterLockSlim LatestTorrentsLock = new();

    public Protocols.DataProvider.Torrent? GetLatestTorrent(ReadOnlySpan<byte> Hash)
    {
        var nativeHash = InfoHash.TruncateV2(Hash);
        LatestTorrentsLock.EnterReadLock();
        try {
            return LatestTorrents.TryGetValue(nativeHash, out var torrent) ? torrent.Clone() : null;
        } finally {
            LatestTorrentsLock.ExitReadLock();
        }
    }

    protected internal override DeltaTorrentsListResponse BuildChanges(HistoryEntry? Previous, HistoryEntry Current)
    {
        var delta = new DeltaTorrentsListResponse();
        foreach (var (hash, torrent) in Current.List) {
            if (Previous == null || !Previous.List.TryGetValue(hash, out var old)) {
                delta.FullUpdate.Add(torrent.Clone());
                continue;
            }

            // These fields need a whole torrent row on the client.
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
                // qBittorrent reports noisy peer counts for inactive completed torrents.
                if (torrent.State.HasFlag(Protocols.DataProvider.TorrentState.Active) &&
                    (torrent.Uploaded != old.Uploaded ||
                     torrent.UPSpeed != old.UPSpeed ||
                     torrent.SeedersTotal != old.SeedersTotal ||
                     torrent.PeersTotal != old.PeersTotal ||
                     torrent.PeersConnected != old.PeersConnected))
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
        var backend = Services.GetRequiredKeyedService<QbitClient>(InstanceKey);
        try {
            var client = await backend.Init();
            var data = await client.GetPartialDataAsync(PollResponseId, CancellationToken);
            var next = new InfoHashDictionary<Protocols.DataProvider.Torrent>();
            if (!data.FullUpdate) {
                LatestTorrentsLock.EnterReadLock();
                try {
                    foreach (var (hash, torrent) in LatestTorrents)
                        next[hash] = torrent;
                } finally {
                    LatestTorrentsLock.ExitReadLock();
                }
            }

            if (data.TorrentsChanged != null) {
                foreach (var (nativeHex, external) in data.TorrentsChanged) {
                    var native = Convert.FromHexString(nativeHex);
                    Protocols.DataProvider.Torrent current;
                    if (next.TryGetValue(native, out var previous)) {
                        current = previous.Clone();
                        // Magnet can acquire a full v2 hash after the first snapshot.
                        if (!String.IsNullOrEmpty(external.InfoHashV2))
                            current.Hash = Convert.FromHexString(external.InfoHashV2).ToByteString();
                    } else {
                        var hash = Grpc.RTSHashFromInfo(external.InfoHashV1, external.InfoHashV2, nativeHex);
                        if (String.IsNullOrEmpty(external.InfoHashV1) && String.IsNullOrEmpty(external.InfoHashV2)) {
                            try {
                                var full = await client.GetTorrentListAsync(new TorrentListQuery {
                                    Hashes = [ nativeHex ]
                                }, CancellationToken);
                                if (full.Count > 0)
                                    hash = Grpc.RTSHashFromInfo(full[0].InfoHashV1, full[0].InfoHashV2, nativeHex);
                            } catch (Exception ex) when (!CancellationToken.IsCancellationRequested) {
                                Logger.LogTrace(ex, "Failed to resolve rtsHash for {Hash}", nativeHex);
                            }
                        }
                        current = new() {
                            Hash = hash.ToByteString()
                        };
                    }
                    TorrentMapper.ApplyFromExternal(current, external);
                    UpdateHashingSpeed(current);
                    next[native] = current;
                }
            }
            if (data.TorrentsRemoved != null) {
                foreach (var native in data.TorrentsRemoved)
                    next.Remove(Convert.FromHexString(native));
            }

            LatestTorrentsLock.EnterWriteLock();
            try {
                LatestTorrents = next;
            } finally {
                LatestTorrentsLock.ExitWriteLock();
            }
            PollResponseId = data.ResponseId;
            return new HistoryEntry(DateTime.UtcNow, next.Values.ToInfoHashDictionary(x => x.Hash.ToByteArray(), x => x.Clone()));
        } catch (Exception ex) {
            PollResponseId = 0;
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
