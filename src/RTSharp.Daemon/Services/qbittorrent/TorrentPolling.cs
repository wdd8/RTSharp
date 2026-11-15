using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Mono.Unix.Native;
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
    // By native hash, with the torrent's key in the snapshot
    private InfoHashDictionary<(Protocols.DataProvider.Torrent Torrent, byte[] Key)> LatestTorrents = new();
    private readonly ReaderWriterLockSlim LatestTorrentsLock = new();
    // Only used by the poll loop
    private InfoHashDictionary<Protocols.DataProvider.Torrent>? LastSnapshot;
    // Moves we asked for, by native hex hash
    private readonly ConcurrentDictionary<string, (string Target, DateTime? Issued)> RequestedMoves = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ActiveMove> ActiveMoves = new(StringComparer.OrdinalIgnoreCase);
    // By native hex hash
    private readonly Dictionary<string, TorrentMapper.StatusParts> StatusParts = new(StringComparer.OrdinalIgnoreCase);

    private sealed class ActiveMove(byte[] NativeHash, string TargetDir, ulong CompletedSize)
    {
        public byte[] NativeHash { get; } = NativeHash;
        public string TargetDir { get; } = TargetDir;
        public ulong CompletedSize { get; } = CompletedSize;
        public SpeedMovingAverage Speed { get; } = new(5);
        public List<(string Path, ulong TotalBytes)>? Pending;
        public ulong TotalCopiedBytes;
        public ulong TotalTargetBytes;
    }

    public Protocols.DataProvider.Torrent? GetLatestTorrent(ReadOnlySpan<byte> Hash)
    {
        var nativeHash = InfoHash.TruncateV2(Hash);
        LatestTorrentsLock.EnterReadLock();
        try {
            return LatestTorrents.TryGetValue(nativeHash, out var latest) ? latest.Torrent.Clone() : null;
        } finally {
            LatestTorrentsLock.ExitReadLock();
        }
    }

    public void MoveRequested(ReadOnlySpan<byte> Hash, string Target) =>
        RequestedMoves[Convert.ToHexString(InfoHash.TruncateV2(Hash))] = (Path.TrimEndingDirectorySeparator(Target), null);

    public void MoveIssued(ReadOnlySpan<byte> Hash)
    {
        var key = Convert.ToHexString(InfoHash.TruncateV2(Hash));
        if (RequestedMoves.TryGetValue(key, out var requested))
            RequestedMoves.TryUpdate(key, requested with { Issued = DateTime.UtcNow }, requested);
    }

    protected internal override DeltaTorrentsListResponse BuildChanges(HistoryEntry? Previous, HistoryEntry Current)
    {
        var delta = new DeltaTorrentsListResponse();
        if (Previous != null && ReferenceEquals(Previous.List, Current.List))
            return delta;

        foreach (var (hash, torrent) in Current.List) {
            if (Previous == null || !Previous.List.TryGetValue(hash, out var old)) {
                delta.FullUpdate.Add(torrent);
                continue;
            }

            if (ReferenceEquals(torrent, old))
                continue;

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
                torrent.Comment != old.Comment ||
                !Equals(torrent.CreatedOn, old.CreatedOn) ||
                !Equals(torrent.FinishedOn, old.FinishedOn) ||
                !torrent.Labels.Equals(old.Labels) ||
                torrent.PrimaryTracker?.Uri != old.PrimaryTracker?.Uri) {
                delta.FullUpdate.Add(torrent);
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
                torrent.IOSpeed != old.IOSpeed ||
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

            if (!data.FullUpdate && LastSnapshot != null && ActiveMoves.Count == 0 && RequestedMoves.IsEmpty && (data.TorrentsChanged?.Count ?? 0) == 0 && (data.TorrentsRemoved?.Count ?? 0) == 0) {
                PollResponseId = data.ResponseId;
                return new HistoryEntry(DateTime.UtcNow, LastSnapshot);
            }

            var next = new InfoHashDictionary<(Protocols.DataProvider.Torrent Torrent, byte[] Key)>(data.FullUpdate ? 0 : LatestTorrents.Count);
            if (data.FullUpdate)
                StatusParts.Clear();
            else {
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
                    string? previousPath = null;
                    if (next.TryGetValue(native, out var previous)) {
                        previousPath = previous.Torrent.RemotePath;
                        current = previous.Torrent.Clone();
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
                    TorrentMapper.ApplyFromExternal(current, external, ref CollectionsMarshal.GetValueRefOrAddDefault(StatusParts, nativeHex, out _));

                    if (current.State.HasFlag(Protocols.DataProvider.TorrentState.Moving)) {
                        if (!ActiveMoves.ContainsKey(nativeHex)) {
                            /*
                             * Convert to active from our own source, otherwise in some cases torrent can
                             * report its new save path as soon as the move starts
                             */
                            if (RequestedMoves.TryRemove(nativeHex, out var requested))
                                ActiveMoves[nativeHex] = new ActiveMove(native, requested.Target, current.CompletedSize);
                            else if (previousPath != null && previousPath != current.RemotePath)
                                ActiveMoves[nativeHex] = new ActiveMove(native, current.RemotePath, current.CompletedSize);
                        }
                    } else {
                        // Moving -> *
                        if (ActiveMoves.Remove(nativeHex, out var ended) && external.CompletedSize == null)
                            current.CompletedSize = ended.CompletedSize;
                    }
                    UpdateHashingSpeed(current);
                    next[native] = (current, current.Hash.ToByteArray());
                }
            }
            if (data.TorrentsRemoved != null) {
                foreach (var native in data.TorrentsRemoved) {
                    next.Remove(Convert.FromHexString(native));
                    ActiveMoves.Remove(native);
                    StatusParts.Remove(native);
                    RequestedMoves.TryRemove(native, out _);
                }
            }

            // Remove lost
            foreach (var (nativeHex, requested) in RequestedMoves) {
                if (requested.Issued is { } issued && DateTime.UtcNow - issued > TimeSpan.FromMinutes(5))
                    RequestedMoves.TryRemove(nativeHex, out _);
            }

            static ulong allocated(string FilePath) => Syscall.stat(FilePath, out var stat) == 0 ? (ulong)stat.st_blocks * 512 : 0;

            foreach (var (nativeHex, move) in ActiveMoves) {
                if (!next.TryGetValue(move.NativeHash, out var entry))
                    continue;

                if (move.Pending == null) {
                    IReadOnlyList<TorrentContent> files;
                    try {
                        files = await client.GetTorrentContentsAsync(nativeHex, null, CancellationToken);
                    } catch (Exception ex) when (!CancellationToken.IsCancellationRequested) {
                        Logger.LogTrace(ex, "Failed to get files of moving torrent {Hash}", nativeHex);
                        continue;
                    }

                    move.Pending = new(files.Count);
                    foreach (var file in files) {
                        if (file.Priority == TorrentContentPriority.Skip)
                            continue;

                        var bytes = (ulong)(file.Progress * file.Size);
                        move.TotalTargetBytes += bytes;
                        move.Pending.Add((Path.Combine(move.TargetDir, file.Name), bytes));
                    }
                }

                // Use allocated bytes due to pre-allocation, but compression might make this unreliable
                ulong copying = 0;
                move.Pending.RemoveAll(x => {
                    var bytes = allocated(x.Path);
                    if (bytes < x.TotalBytes) {
                        copying += bytes;
                        return false;
                    }
                    move.TotalCopiedBytes += x.TotalBytes;
                    return true;
                });

                var copied = move.TotalCopiedBytes + copying;
                move.Speed.Value(DateTime.UtcNow, (long)copied);
                var speed = (ulong)move.Speed.CalculateSpeed();
                Duration? eta = null;
                if (speed != 0) {
                    var secs = (double)(move.TotalTargetBytes - copied) / speed;
                    eta = secs >= Duration.MaxSeconds ? null : Duration.FromTimeSpan(TimeSpan.FromSeconds(secs));
                }

                var completed = move.TotalTargetBytes == 0 ? move.CompletedSize : (ulong)((double)move.CompletedSize * copied / move.TotalTargetBytes);
                if (entry.Torrent.IOSpeed == speed && Equals(entry.Torrent.ETA, eta) && entry.Torrent.CompletedSize == completed)
                    continue;

                var moving = entry.Torrent.Clone();
                moving.IOSpeed = speed;
                moving.ETA = eta;
                moving.CompletedSize = completed;
                next[move.NativeHash] = (moving, entry.Key);
            }

            LatestTorrentsLock.EnterWriteLock(); try {
                LatestTorrents = next;
            } finally { LatestTorrentsLock.ExitWriteLock(); }
            PollResponseId = data.ResponseId;
            LastSnapshot = next.Values.ToInfoHashDictionary(x => x.Key, x => x.Torrent);
            return new HistoryEntry(DateTime.UtcNow, LastSnapshot);
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
            var hashingSpeed = (ulong)sma.CalculateSpeed();
            Torrent.IOSpeed = hashingSpeed;

            // No native ETA for hashing state
            if (hashingSpeed == 0 || Torrent.IsCompleted) {
                Torrent.ETA = null;
            } else {
                var secs = (double)(Torrent.WantedSize - Torrent.CompletedSize) / hashingSpeed;
                Torrent.ETA = secs >= Duration.MaxSeconds ? null : Duration.FromTimeSpan(TimeSpan.FromSeconds(secs));
            }
        } else {
            Torrent.IOSpeed = 0;
        }
    }
}
