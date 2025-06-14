using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using QBittorrent.Client;

using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using Transmission.Net.Api;
using Transmission.Net.Api.Entity;
using Transmission.Net.Core.Enums;

namespace RTSharp.Daemon.Services.transmission
{
    public class Grpc
    {
        TransmissionClient Client;
        SessionsService Sessions;
        ILogger Logger;
        IServiceProvider ServiceProvider;
        string InstanceKey;

        public Grpc([ServiceKey] string InstanceKey, SessionsService Sessions, ILogger<Grpc> Logger, IServiceProvider ServiceProvider)
        {
            Client = ServiceProvider.GetRequiredKeyedService<TransmissionClient>(InstanceKey);
            this.InstanceKey = InstanceKey;
            this.ServiceProvider = ServiceProvider;
            this.Sessions = Sessions;
            this.Logger = Logger;

            HashSpanToId = HashToId.GetAlternateLookup<ReadOnlySpan<byte>>();
        }

        private ConcurrentInfoHashDictionary<Protocols.DataProvider.Torrent> LatestTorrents = new();

        private ConcurrentInfoHashDictionary<int> HashToId { get; } = new();
        private ConcurrentDictionary<byte[], int>.AlternateLookup<ReadOnlySpan<byte>> HashSpanToId { get; }
        private ConcurrentDictionary<int, byte[]> IdToHash { get; } = new();
        private ConcurrentQueue<byte[]> FullUpdateQueue = new();

        int? Translate(byte[] In)
        {
            if (HashToId.TryGetValue(In, out var outp)) {
                return outp;
            }

            return null;
        }

        int? Translate(ReadOnlySpan<byte> In)
        {
            if (HashSpanToId.TryGetValue(In, out var outp)) {
                return outp;
            }

            return null;
        }

        int[] Translate(IList<Protocols.DataProvider.Torrent> In)
        {
            var ret = new int[In.Count];
            for (var x = 0;x < ret.Length;x++) {
                var translated = Translate(In[x].Hash.ToByteArray()) ?? throw new NullReferenceException($"Cannot translate '{Convert.ToHexString(In[x].Hash.Span)}'");
                ret[x] = translated;
            }

            return ret;
        }

        object[] TranslateObj(IList<byte[]> In)
        {
            var ret = new object[In.Count];

            for (var x = 0;x < In.Count;x++) {
                var translated = Translate(In[x]) ?? throw new NullReferenceException($"Cannot translate '{Convert.ToHexString(In[x])}'");
                ret[x] = translated;
            }

            return ret;
        }

        int[] Translate(IList<byte[]> In)
        {
            var ret = new int[In.Count];

            for (var x = 0;x < In.Count;x++) {
                var translated = Translate(In[x]) ?? throw new NullReferenceException($"Cannot translate '{Convert.ToHexString(In[x])}'");
                ret[x] = translated;
            }

            return ret;
        }

        int[] Translate(Torrents In)
        {
            var hashes = In.Hashes;
            var ret = new int[hashes.Count];

            for (var x = 0;x < hashes.Count;x++) {
                var translated = Translate(hashes[x].Span);
                if (translated == null) {
                    throw new NullReferenceException($"Cannot translate '{Convert.ToHexString(hashes[x].Span)}'");
                }
                ret[x] = translated.Value;
            }

            return ret;
        }

        object[] TranslateObj(Torrents In)
        {
            var hashes = In.Hashes;
            var ret = new object[hashes.Count];

            for (var x = 0;x < hashes.Count;x++) {
                var translated = Translate(hashes[x].Span);
                if (translated == null) {
                    throw new NullReferenceException($"Cannot translate '{Convert.ToHexString(hashes[x].Span)}'");
                }
                ret[x] = translated.Value;
            }

            return ret;
        }

        private readonly string[] ALL_FIELDS_EXCEPT_LISTS = TorrentFields.ALL_FIELDS.Except([TorrentFields.FILES, TorrentFields.FILE_STATS, TorrentFields.PEERS, TorrentFields.TRACKER_STATS, TorrentFields.TRACKER_LIST, TorrentFields.PIECES]).ToArray();

        public async Task GetTorrentListUpdates(GetTorrentListUpdatesRequest Req, IServerStreamWriter<DeltaTorrentsListResponse> Res, CancellationToken CancellationToken)
        {
            await Client.Init();

            var pollInternal = Req.Interval.ToTimeSpan();

            var delta = new DeltaTorrentsListResponse();

            var all = await Client.Client.TorrentGetAsync(null, ALL_FIELDS_EXCEPT_LISTS);

            foreach (var externalTorrent in all!.Torrents) {
                var hash = Convert.FromHexString(externalTorrent.HashString!);
                var internalTorrent = new Protocols.DataProvider.Torrent {
                    Hash = hash.ToByteString()
                };
                TorrentMapper.ApplyFromExternal(internalTorrent, externalTorrent);

                delta.FullUpdate.Add(internalTorrent);
                LatestTorrents[hash] = internalTorrent;
                HashToId[hash] = externalTorrent.Id!.Value;
                IdToHash[externalTorrent.Id!.Value] = hash;
            }

            await Res.WriteAsync(delta, CancellationToken);

            while (!CancellationToken.IsCancellationRequested) {
                delta.FullUpdate.Clear();
                delta.Incomplete.Clear();
                delta.Complete.Clear();
                delta.Removed.Clear();

                void fullUpdate(byte[] Hash, TorrentView Input)
                {
                    var internalTorrent = new Protocols.DataProvider.Torrent {
                        Hash = Hash.ToByteString()
                    };
                    TorrentMapper.ApplyFromExternal(internalTorrent, Input);

                    LatestTorrents[Hash] = internalTorrent;
                    HashToId[Hash] = Input.Id!.Value;
                    IdToHash[Input.Id!.Value] = Hash;
                    delta.FullUpdate.Add(internalTorrent);
                }

                List<int>? fullUpdateQueue = null;
                while (FullUpdateQueue.TryDequeue(out var toFullUpdate)) {
                    fullUpdateQueue ??= new();
                    fullUpdateQueue.Add(Translate(toFullUpdate) ?? throw new NullReferenceException($"Cannot translate '{toFullUpdate}'"));
                }

                Task<TorrentsResult?>? fullUpdateResultTask = null;
                if (fullUpdateQueue != null) {
                    fullUpdateResultTask = Client.Client.TorrentGetAsync([.. fullUpdateQueue], ALL_FIELDS_EXCEPT_LISTS);
                }
                var list = await Client.Client.TorrentGetRecentyActiveAsync(ALL_FIELDS_EXCEPT_LISTS);
                
                if (fullUpdateResultTask != null) {
                    var fullUpdateResult = await fullUpdateResultTask;
                    list!.Torrents = [.. list!.Torrents, .. fullUpdateResult!.Torrents.ExceptBy(list!.Torrents.Select(x => x.Id), x => x.Id)];
                    if (fullUpdateResult.Removed != null) {
                        list!.Removed ??= [];
                        list!.Removed = [.. list!.Removed, .. fullUpdateResult.Removed.Except(list!.Removed)];
                    }
                }

                foreach (var changedTorrent in list!.Torrents) {
                    var hash = Convert.FromHexString(changedTorrent.HashString!);
                    Protocols.DataProvider.Torrent internalTorrent;

                    if (!LatestTorrents.TryGetValue(hash, out var torrent)) {
                        Logger.LogInformation($"New torrent {Convert.ToHexString(hash)} detected: {changedTorrent.Name}");
                        fullUpdate(hash, changedTorrent);
                        continue;
                    } else {
                        internalTorrent = torrent;

                        var prevState = internalTorrent.State;
                        TorrentMapper.ApplyFromExternal(internalTorrent, changedTorrent);

                        if (internalTorrent.State != prevState) {
                            LatestTorrents[hash] = internalTorrent;
                            HashToId[hash] = changedTorrent.Id!.Value;
                            IdToHash[changedTorrent.Id!.Value] = hash;

                            delta.FullUpdate.Add(internalTorrent);
                            continue;
                        } else {
                            LatestTorrents[hash] = internalTorrent;
                            HashToId[hash] = changedTorrent.Id!.Value;
                            IdToHash[changedTorrent.Id!.Value] = hash;
                        }
                    }

                    if (internalTorrent.CompletedSize != internalTorrent.WantedSize) {
                        delta.Incomplete.Add(new IncompleteDeltaTorrentResponse {
                            Hash = internalTorrent.Hash,
                            State = internalTorrent.State,
                            WantedSize = internalTorrent.WantedSize,
                            Downloaded = internalTorrent.Downloaded,
                            CompletedSize = internalTorrent.CompletedSize,
                            Uploaded = internalTorrent.Uploaded,
                            DLSpeed = internalTorrent.DLSpeed,
                            UPSpeed = internalTorrent.UPSpeed,
                            ETA = internalTorrent.ETA,
                            Labels = { internalTorrent.Labels },
                            RemotePath = internalTorrent.RemotePath,
                            SeedersConnected = internalTorrent.SeedersConnected,
                            SeedersTotal = internalTorrent.SeedersTotal,
                            PeersConnected = internalTorrent.PeersConnected,
                            PeersTotal = internalTorrent.PeersTotal,
                            Priority = internalTorrent.Priority,
                            Wasted = internalTorrent.Wasted,
                            PrimaryTracker = internalTorrent.PrimaryTracker,
                            StatusMessage = internalTorrent.StatusMessage,
                            MagnetDummy = internalTorrent.MagnetDummy
                        });
                    } else {
                        delta.Complete.Add(new CompleteDeltaTorrentResponse {
                            Hash = internalTorrent.Hash,
                            State = internalTorrent.State,
                            WantedSize = internalTorrent.WantedSize,
                            Uploaded = internalTorrent.Uploaded,
                            UPSpeed = internalTorrent.UPSpeed,
                            Labels = { internalTorrent.Labels },
                            RemotePath = internalTorrent.RemotePath,
                            FinishedOn = internalTorrent.FinishedOn,
                            SeedersTotal = internalTorrent.SeedersTotal,
                            PeersTotal = internalTorrent.PeersTotal,
                            PeersConnected = internalTorrent.PeersConnected,
                            Priority = internalTorrent.Priority,
                            PrimaryTracker = internalTorrent.PrimaryTracker,
                            StatusMessage = internalTorrent.StatusMessage
                        });
                    }
                }

                if (list.Removed != null) {
                    foreach (var id in list.Removed) {
                        var hash = IdToHash[id];
                        delta.Removed.Add(hash.ToByteString());
                        LatestTorrents.TryRemove(hash, out _);
                    }
                }

                if (delta.Incomplete.Count > 0 || delta.Complete.Count > 0 || delta.Removed.Count > 0 || delta.FullUpdate.Count > 0)
                    await Res.WriteAsync(delta, CancellationToken);
                await Task.Delay(pollInternal, CancellationToken);
            }
        }

        public async Task<TorrentsListResponse> GetTorrentList()
        {
            await Client.Init();

            var all = await Client.Client.TorrentGetAsync(null, TorrentFields.ALL_FIELDS);

            var ret = new TorrentsListResponse {
                List = {
                    all.Torrents.Select(x => {
                        var internalTorrent = new Protocols.DataProvider.Torrent {
                            Hash = Convert.FromHexString(x.HashString!).ToByteString()
                        };
                        TorrentMapper.ApplyFromExternal(internalTorrent, x);
                        return internalTorrent;
                    })
                }
            };
            return ret;
        }

        public async Task<TorrentsReply> PerformVoidActionObj(Torrents In, Func<object[], Task> Fx)
        {
            await Client.Init();

            var tasks = new List<Task<TorrentsReply.Types.TorrentReply>>();
            Exception exception = null;

            try {
                await Fx(TranslateObj(In));
            } catch (Exception ex) {
                exception = ex;
            }

            return new TorrentsReply {
                Torrents = {
                    In.Hashes.Select(x => new TorrentsReply.Types.TorrentReply {
                        InfoHash = x.ToByteArray().ToByteString(),
                        Status = {
                            exception == null ? (IList<Protocols.DataProvider.Status>)Array.Empty<Protocols.DataProvider.Status>() : [
                                new Protocols.DataProvider.Status {
                                    Command = "",
                                    FaultString = exception.Message,
                                    FaultCode = "1"
                                }
                            ]
                        }
                    })
                }
            };
        }

        public async Task<TorrentsReply> PerformVoidActionInt(Torrents In, Func<int[], Task> Fx)
        {
            await Client.Init();

            var tasks = new List<Task<TorrentsReply.Types.TorrentReply>>();
            Exception exception = null;

            try {
                await Fx(Translate(In));
            } catch (Exception ex) {
                exception = ex;
            }

            return new TorrentsReply {
                Torrents = {
                    In.Hashes.Select(x => new TorrentsReply.Types.TorrentReply {
                        InfoHash = x.ToByteArray().ToByteString(),
                        Status = {
                            exception == null ? (IList<Protocols.DataProvider.Status>)Array.Empty<Protocols.DataProvider.Status>() : [
                                new Protocols.DataProvider.Status {
                                    Command = "",
                                    FaultString = exception.Message,
                                    FaultCode = "1"
                                }
                            ]
                        }
                    })
                }
            };
        }

        public Task<TorrentsReply> StartTorrents(Torrents In) => PerformVoidActionObj(In, Client.Client.TorrentStartAsync);

        public Task<TorrentsReply> PauseTorrents(Torrents In) => throw new NotSupportedException();

        public Task<TorrentsReply> StopTorrents(Torrents In) => PerformVoidActionObj(In, Client.Client.TorrentStopAsync);

        public async Task<BytesValue> ForceRecheckTorrents(Torrents In, CancellationToken CancellationToken)
        {
            await Client.Init();

            Exception? exception = null;

            try {
                await Client.Client.TorrentVerifyAsync(TranslateObj(In));
            } catch (Exception ex) {
                exception = ex;
            }

            Logger.LogInformation("Recheck sent");

            var res = In.Hashes.Select(x => (Hash: x.ToByteArray(), Exceptions: exception == null ? (IList<Exception>)Array.Empty<Exception>() : ([exception]))).ToArray();

            var hashes = In.Hashes.Select(x => x.ToByteArray()).ToArray();

            var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

            var session = Sessions.CreateSession(cts, async (session) => {
                session.Progress.Text = "Observing state...";
                session.Progress.Progress = 0f;
                session.Progress.State = TASK_STATE.RUNNING;

                var sw = Stopwatch.StartNew();

                var stateObserved = new InfoHashDictionary<TorrentStatus?>();
                var stateObserved2 = new InfoHashDictionary<TorrentStatus?>();
                foreach (var torrentResult in res) {
                    stateObserved[torrentResult.Hash] = null;
                    stateObserved2[torrentResult.Hash] = null;
                }

                while (true) {
                    if (cts.IsCancellationRequested)
                        break;

                    Logger.LogDebug("Getting partial data inside session");

                    var partialData = await Client.Client.TorrentGetRecentyActiveAsync(TorrentFields.HASH_STRING, TorrentFields.STATUS);

                    Logger.LogDebug("Got partial data inside session");

                    foreach (var hash in hashes) {
                        var hashStr = Convert.ToHexStringLower(hash);

                        var torrentState = partialData!.Torrents?.FirstOrDefault(x => x.HashString!.Equals(hashStr, StringComparison.OrdinalIgnoreCase))?.Status;

                        if (torrentState == null) {
                            Logger.LogDebug($"Torrent {hashStr} no changes");
                            continue;
                        }

                        Logger.LogDebug($"Torrent {hashStr} state: {torrentState.Value}");

                        var currentValue =
                            torrentState.Value == TorrentStatus.Verifying ||
                            torrentState.Value == TorrentStatus.VerifyQueue ? TorrentStatus.Verifying : torrentState.Value;

                        if (stateObserved2[hash] != currentValue) {
                            stateObserved[hash] = stateObserved2[hash];
                            stateObserved2[hash] = currentValue;

                            Logger.LogDebug($"State transition for {hashStr}: {stateObserved[hash]} -> {stateObserved2[hash]}");
                        }
                    }

                    if (sw.Elapsed < TimeSpan.FromSeconds(5)) {
                        // Torrents at this point are seeding (did not enter hashing state), hashing, or completed hashing

                        bool pass = true;
                        foreach (var hash in hashes) {
                            var s = stateObserved[hash];
                            var s2 = stateObserved2[hash];

                            // Can't retrieve
                            if (s == null && s2 == null) {
                                Logger.LogDebug($"(2) Cannot retrieve state for {Convert.ToHexStringLower(hash)}");
                                continue;
                            }

                            // Only one state transition
                            if (s == null && s2 != null) {
                                Logger.LogDebug($"(2) {Convert.ToHexStringLower(hash)} only one state transision");
                                pass = false;
                                break;
                            }

                            // Hashing
                            if (s2 == TorrentStatus.Verifying) {
                                Logger.LogDebug($"(2) {Convert.ToHexStringLower(hash)} still hashing");
                                pass = false;
                                break;
                            }

                            // Still hashing
                            pass = false;
                            break;
                        }

                        if (pass) {
                            Logger.LogDebug("(2) Observed all");
                            break;
                        }
                    } else if (sw.Elapsed < TimeSpan.FromMinutes(10)) {
                        // Torrents at this point should be hashing or completed hashing

                        bool pass = true;
                        foreach (var hash in hashes) {
                            var s = stateObserved[hash];
                            var s2 = stateObserved2[hash];

                            // Can't retrieve
                            if (s == null && s2 == null) {
                                Logger.LogDebug($"(1) Cannot retrieve state for {Convert.ToHexStringLower(hash)}");
                                continue;
                            }

                            // Still hashing
                            if (s2 == TorrentStatus.Verifying) {
                                Logger.LogDebug($"(1) {Convert.ToHexStringLower(hash)} still hashing");
                                pass = false;
                                break;
                            }
                        }

                        if (pass) {
                            Logger.LogDebug("(1) Observed all");
                            session.Progress.State = TASK_STATE.DONE;
                            break;
                        }
                    } else {
                        Logger.LogWarning("Finishing checking for rehash state after 10 minutes");
                        session.Progress.State = TASK_STATE.FAILED;
                        break;
                    }

                    await Task.Delay(1000); // TODO: Customizable?
                }

                Logger.LogDebug("Session complete");
            });

            Logger.LogDebug("Returning");

            return new BytesValue {
                Value = session.Id.ToByteArray().ToByteString()
            };
        }

        public Task<TorrentsReply> ReannounceToAllTrackers(Torrents In) => PerformVoidActionObj(In, Client.Client.TorrentReannounceAsync);

        record Torrent(string Path, string Filename, MemoryStream Data);

        public async Task<TorrentsReply> AddTorrents(IAsyncStreamReader<NewTorrentsData> Req)
        {
            await Client.Init();

            var torrents = new Dictionary<string, Torrent>();
            await foreach (var chunk in Req.ReadAllAsync()) {
                switch (chunk.DataCase) {
                    case NewTorrentsData.DataOneofCase.TMetadata:
                        torrents[chunk.TMetadata.Id] = new Torrent(chunk.TMetadata.Path, chunk.TMetadata.Filename, new MemoryStream());
                        break;
                    case NewTorrentsData.DataOneofCase.TData:
                        torrents[chunk.TData.Id].Data.Write(chunk.TData.Chunk.ToByteArray());
                        break;
                }
            }

            var parser = new BencodeNET.Torrents.TorrentParser();
            var ret = new TorrentsReply();

            foreach (var (id, torrent) in torrents) {
                torrent.Data.Position = 0;

                BencodeNET.Torrents.Torrent btorrent;
                try {
                    btorrent = parser.Parse(torrent.Data);
                } catch (Exception ex) {
                    ret.Torrents.Add(new TorrentsReply.Types.TorrentReply() {
                        InfoHash = Array.Empty<byte>().ToByteString(),
                        Status = { new Protocols.DataProvider.Status() {
                            Command = "",
                            FaultCode = "1",
                            FaultString = $"Invalid bencode on torrent id {id}"
                        } }
                    });
                    Logger.LogError(ex, "Invalid bencode");
                    continue;
                }

                try {
                    var b64 = Convert.ToBase64String(torrent.Data.ToArray());

                    var newInfo = await Client.Client.TorrentAddAsync(new NewTorrent {
                        DownloadDirectory = torrent.Path,
                        Metainfo = b64,
                        Paused = true
                    });
                    HashToId[btorrent.OriginalInfoHashBytes] = newInfo!.Id;
                } catch (Exception ex) {
                    ret.Torrents.Add(new TorrentsReply.Types.TorrentReply() {
                        InfoHash = Array.Empty<byte>().ToByteString(),
                        Status = { new Protocols.DataProvider.Status() {
                            Command = "",
                            FaultCode = "1",
                            FaultString = $"Invalid bencode on torrent id {id}"
                        } }
                    });
                    continue;
                }

                ret.Torrents.Add(new TorrentsReply.Types.TorrentReply() {
                    InfoHash = btorrent.OriginalInfoHashBytes.ToByteString(),
                    Status = {
                        new Protocols.DataProvider.Status() {
                            Command = "",
                            FaultCode = "0",
                            FaultString = ""
                        }
                    }
                });
            }

            return ret;
        }

        public async Task<TorrentsFilesReply> GetTorrentsFiles(Torrents Req)
        {
            await Client.Init();

            // Could also fetch from torrent polling task if its live
            var torrents = (await Client.Client.TorrentGetAsync(Translate(Req), ["files", "hashString", "name", "fileStats", "pieceSize"]))!.Torrents;

            return new TorrentsFilesReply {
                Reply = {
                    torrents.Select(x => new TorrentsFilesReply.Types.TorrentsFiles {
                        InfoHash = Convert.FromHexString(x.HashString!).ToByteString(),
                        MultiFile = x.Files![0].Name!.Contains('/'),
                        Files = {
                            x.Files.Select((i, idx) => {
                                var torrentName = x.Name!;

                                i.Name = i.Name!.StartsWith(torrentName + '/') ? i.Name[(torrentName.Length + 1)..] : i.Name;

                                return new TorrentsFilesReply.Types.File {
                                    Path = i.Name,
                                    Size = (ulong)i.Length!,
                                    DownloadedPieces = (ulong)(i.BytesCompleted! / (double)x.PieceSize!),
                                    Downloaded = (ulong)i.BytesCompleted!,
                                    Priority = !x.FileStats![idx].Wanted ? TorrentsFilesReply.Types.FilePriority.DontDownload : x.FileStats[idx].Priority switch {
                                        Priority.Low => TorrentsFilesReply.Types.FilePriority.Normal, // TODO: missing
                                        Priority.Normal => TorrentsFilesReply.Types.FilePriority.Normal,
                                        Priority.High => TorrentsFilesReply.Types.FilePriority.High,
                                        _ => throw new NotImplementedException()
                                    }
                                };
                            })
                        }
                    })
                }
            };
        }

        public Task<TorrentsReply> RemoveTorrents(Torrents In) => PerformVoidActionInt(In, x => Client.Client.TorrentRemoveAsync(x, false));

        public Task<TorrentsReply> RemoveTorrentsAndData(Torrents In) => PerformVoidActionInt(In, x => Client.Client.TorrentRemoveAsync(x, true));

        public async Task GetDotTorrents(Torrents Req, IServerStreamWriter<DotTorrentsData> Res, CancellationToken CancellationToken)
        {
            await Client.Init();

            for (var x = 0;x < Req.Hashes.Count;x++) {
                var hash = Req.Hashes[x];
                var idx = x.ToString();

                await Res.WriteAsync(new DotTorrentsData {
                    TMetadata = new DotTorrentsData.Types.Metadata {
                        Id = idx,
                        Hash = hash
                    }
                }, CancellationToken);

                try {
                    var torrentPath = Path.Combine(Client.ConfigDir, "torrents", Convert.ToHexStringLower(hash.Span) + ".torrent");

                    var stm = System.IO.File.OpenRead(torrentPath);

                    int read = 0;
                    Memory<byte> buffer = new byte[4096];
                    while ((read = await stm.ReadAsync(buffer, CancellationToken)) != 0) {
                        await Res.WriteAsync(new DotTorrentsData {
                            TData = new DotTorrentsData.Types.TorrentData {
                                Id = idx,
                                Chunk = buffer.Span[..read].ToByteString()
                            }
                        }, CancellationToken);
                    }
                } catch (Exception ex) {
                    Logger.LogError(ex, $"Failed to get .torrent for {Convert.ToHexString(hash.Span)}");

                    await Res.WriteAsync(new DotTorrentsData {
                        TData = new DotTorrentsData.Types.TorrentData {
                            Id = idx,
                            Chunk = ByteString.Empty
                        }
                    }, CancellationToken);
                }
            }
        }

        public async Task<TorrentsPeersReply> GetTorrentsPeers(Torrents Req)
        {
            await Client.Init();

            var torrents = await Client.Client.TorrentGetAsync(Translate(Req), [TorrentFields.HASH_STRING, TorrentFields.PEERS, TorrentFields.TOTAL_SIZE]);

            return new TorrentsPeersReply {
                Reply = {
                    torrents!.Torrents.Select(x => {
                        using var scope = ServiceProvider.CreateScope();
                        var smas = scope.ServiceProvider.GetRequiredService<ISpeedMovingAverageService>();

                        return new TorrentsPeersReply.Types.TorrentsPeers {
                            InfoHash = Convert.FromHexString(x.HashString!).ToByteString(),
                            Peers = {
                                x.Peers!.Select(i => {
                                    ulong peerDLSpeed = 0;

                                    if (i.Progress != 1) {
                                        var sma = smas.Get(InstanceKey, $"PeerDLSpeed_{x.HashString}_{i.Address}");

                                        if (i.Progress != null) {
                                            sma.ValueIfChanged((long)(x.TotalSize! * i.Progress!));
                                        }

                                        peerDLSpeed = (ulong)sma.CalculateSpeed();
                                    }

                                    TorrentsPeersReply.Types.PeerFlags mapFlag(char Flag)
                                    {
                                        return Flag switch {
                                            '?' => 0, // We unchoked this peer, but they're not interested
                                            'D' => 0, // Downloading from this peer
                                            'E' => TorrentsPeersReply.Types.PeerFlags.EEncrypted,
                                            'H' => 0, // Peer was discovered through Distributed Hash Table (DHT)
                                            'I' => TorrentsPeersReply.Types.PeerFlags.IIncoming,
                                            'K' => TorrentsPeersReply.Types.PeerFlags.UUnwanted, // Peer has unchoked us, but we're not interested
                                            'O' => 0, // Optimistic unchoke
                                            'T' => 0, // Peer is connected via uTP
                                            'U' => 0, // Uploading to peer
                                            'X' => 0, // Peer was discovered through Peer Exchange (PEX)
                                            'd' => TorrentsPeersReply.Types.PeerFlags.PPreferred, // We would download from this peer if they'd let us
                                            'u' => TorrentsPeersReply.Types.PeerFlags.PPreferred, // We would upload to this peer if they'd ask
                                            _ => 0
                                        };
                                    }

                                    var flagArr = i.FlagStr!.Where(x => !Char.IsWhiteSpace(x)).Select(mapFlag);
                                    TorrentsPeersReply.Types.PeerFlags flags = 0;

                                    if (flagArr.Any())
                                        flags = flagArr.Aggregate((a, b) => a | b);

                                    return new TorrentsPeersReply.Types.Peer {
                                        PeerID = Encoding.UTF8.GetBytes(i.Address!).ToByteString(),
                                        IPAddress = IPAddress.Parse(i.Address!).GetAddressBytes().ToByteString(),
                                        Port = (uint)i.Port!,
                                        Client = i.ClientName,
                                        Flags = flags,
                                        Done = (float)(i.Progress! * 100f),
                                        Downloaded = 0UL,
                                        Uploaded = 0UL,
                                        DLSpeed = (ulong)i.RateToClient!,
                                        UPSpeed = (ulong)i.RateToPeer!,
                                        PeerDLSpeed = peerDLSpeed
                                    };
                                })
                            }
                        };
                    })
                }
            };
        }

        public async Task<TorrentsTrackersReply> GetTorrentsTrackers(Torrents Req, CancellationToken CancellationToken)
        {
            await Client.Init();

            var torrents = await Client.Client.TorrentGetAsync(Translate(Req), [TorrentFields.HASH_STRING, TorrentFields.TRACKER_STATS]);

            return new TorrentsTrackersReply {
                Reply = {
                    await Task.WhenAll(torrents.Torrents.Select(async x => {
                        var trackers = x.TrackerStats;

                        return new TorrentsTrackersReply.Types.TorrentsTrackers {
                            InfoHash = Convert.FromHexString(x.HashString!).ToByteString(),
                            Trackers = {
                                trackers.Select(x => new Protocols.DataProvider.TorrentTracker {
                                    ID = x.Id.ToString(),
                                    Uri = x.Announce,
                                    Status = (x.AnnounceState == null ? x.ScrapeState : x.AnnounceState) switch {
                                        TrackerState.Inactive => Protocols.DataProvider.TorrentTrackerStatus.NotActive,
                                        TrackerState.Waiting => Protocols.DataProvider.TorrentTrackerStatus.Enabled | Protocols.DataProvider.TorrentTrackerStatus.NotContactedYet,
                                        TrackerState.Queued => Protocols.DataProvider.TorrentTrackerStatus.Enabled | Protocols.DataProvider.TorrentTrackerStatus.NotContactedYet,
                                        TrackerState.Active => Protocols.DataProvider.TorrentTrackerStatus.Enabled | Protocols.DataProvider.TorrentTrackerStatus.Active,
                                        _ => Protocols.DataProvider.TorrentTrackerStatus.Placeholder
                                    },
                                    Seeders = (uint)(x.SeederCount == -1 ? 0 : x.SeederCount)!,
                                    Peers = (uint)(x.LeecherCount == -1 ? 0 : x.LeecherCount)!,
                                    Downloaded = (uint)x.LastAnnouncePeerCount!,
                                    LastUpdated = Timestamp.FromDateTime(x.LastAnnounceTime ?? x.LastScrapeTime!.Value),
                                    ScrapeInterval = x.NextAnnounceTime == null || x.LastAnnounceTime == null ? Duration.FromTimeSpan(TimeSpan.Zero) : Duration.FromTimeSpan(x.NextAnnounceTime.Value - x.LastAnnounceTime.Value),
                                    StatusMessage = x.LastAnnounceResult ?? x.LastScrapeResult ?? ""
                                })
                            }
                        };
                    }))
                }
            };
        }

        public async Task<BytesValue> MoveDownloadDirectory(MoveDownloadDirectoryArgs Req, CancellationToken CancellationToken)
        {
            await Client.Init();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

            if (!Req.DeleteSourceFiles) {
                throw new ArgumentException("DeleteSourceFiles = false is not supported");
            }

            async Task fx(ScriptSession session)
            {
                session.Progress.State = TASK_STATE.RUNNING;
                session.Progress.Progress = 0f;

                var enumFiles = new ScriptProgressState(session) {
                    Text = "Enumerating files...",
                    Progress = 0f,
                    State = TASK_STATE.WAITING
                };

                var tasks = new List<Task<(ByteString Hash, IList<Exception> Exceptions)>>();
                foreach (var req in Req.Torrents) {
                    tasks.Add(Task.Run(async () => {
                        Exception? exception = null;

                        ScriptProgressState progress;
                        session.Progress.Chain =
                        [
                            ..(session.Progress.Chain ?? []),
                            progress = new ScriptProgressState(session) {
                                Text = $"Setting location for {Convert.ToHexString(req.InfoHash.Span)} to {req.TargetDirectory}",
                                Progress = 0f,
                                State = TASK_STATE.RUNNING
                            },
                        ];

                        try {
                            await Client.Client.TorrentSetLocationAsync([Translate(req.InfoHash.Span)!.Value], req.TargetDirectory, Req.Move);
                        } catch (Exception ex) {
                            progress.State = TASK_STATE.FAILED;
                            progress.Text = $"Failed to set location. {ex.Message}";
                            progress.StateData = ex.ToString();
                            progress.Progress = 100f;

                            session.Progress.State = progress.State;
                            session.Progress.Text = progress.Text;
                            session.Progress.StateData = progress.StateData;
                            cts.Cancel();
                        }

                        progress.Progress = 100f;
                        session.Progress.Progress = tasks.Where(x => x.Status != TaskStatus.RanToCompletion).Count() / (float)tasks.Count * 100;

                        return (req.InfoHash, exception == null ? (IList<Exception>)Array.Empty<Exception>() : [exception]);
                    }));
                }

                await Task.WhenAll(tasks);

                session.Progress.State = TASK_STATE.DONE;
                session.Progress.StateData = "";
                session.Progress.Text = "Done";
            }

            var session = Sessions.CreateSession(cts, fx);

            return session.Id.ToByteArray().ToBytesValue();
        }

        public async Task<TorrentsReply> SetLabels(SetLabelsArgs Req)
        {
            await Client.Init();

            Logger.LogInformation("In SetLabels");

            var tasks = new List<Task<IEnumerable<TorrentsReply.Types.TorrentReply>>>();

            foreach (var pair in Req.In.GroupBy(x => [.. x.Labels], new StringArrayComparer())) {
                var hashes = pair.Select(x => x.InfoHash.ToByteArray());
                var labels = pair.Key;
                async Task<IEnumerable<TorrentsReply.Types.TorrentReply>> setLabels(IEnumerable<byte[]> Hashes, string[] Labels)
                {
                    Exception? exception = null;

                    try {
                        await Client.Client.TorrentSetAsync(new Transmission.Net.Arguments.TorrentSettings {
                            IDs = TranslateObj(Hashes.ToArray()),
                            Labels = Labels.ToArray()
                        });
                    } catch (Exception ex) {
                        exception = ex;
                    }

                    Logger.LogInformation($"Labels set: {String.Join(", ", Labels)}. Ex: {exception}");

                    return hashes.Select(x => new TorrentsReply.Types.TorrentReply {
                        InfoHash = x.ToByteString(),
                        Status = {
                            exception == null ? (IList<Protocols.DataProvider.Status>)Array.Empty<Protocols.DataProvider.Status>() : [
                                new Protocols.DataProvider.Status {
                                    Command = "",
                                    FaultString = exception.Message,
                                    FaultCode = "1"
                                }
                            ]
                        }
                    });
                }

                tasks.Add(setLabels(hashes, labels));
            }

            var completeTasks = await Task.WhenAll(tasks);

            return new TorrentsReply {
                Torrents = {
                    completeTasks.SelectMany(x => x)
                }
            };
        }

        public async Task<TorrentsPiecesReply> GetTorrentsPieces(Torrents Req)
        {
            await Client.Init();

            var torrents = await Client.Client.TorrentGetAsync(Translate(Req), [ TorrentFields.HASH_STRING, TorrentFields.PIECES ]);

            return new TorrentsPiecesReply {
                Reply = {
                    torrents!.Torrents.Select(x => {
                        var pieces = Convert.FromBase64String(x.Pieces!);

                        return new TorrentsPiecesReply.Types.TorrentsPieces {
                            InfoHash = Convert.FromHexString(x.HashString!).ToByteString(),
                            Bitfield = pieces.Length == 0 ? ByteString.CopyFrom(0) : ByteString.CopyFrom(pieces)
                        };
                    })
                }
            };
        }

        public async Task<Protocols.DataProvider.Torrent> GetTorrent(BytesValue Hash)
        {
            await Client.Init();

            var id = Translate(Hash.Value.Span) ?? throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, "Torrent translation not found"));

            var torrent = await Client.Client.TorrentGetAsync([ id ]);

            if (!torrent!.Torrents.Any())
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, "Torrent not found"));

            var internalTorrent = new Protocols.DataProvider.Torrent {
                Hash = Hash.ToByteString()
            };
            TorrentMapper.ApplyFromExternal(internalTorrent, torrent.Torrents.First());
            return internalTorrent;
        }

        public Task<Empty> QueueTorrentUpdate(Torrents Req)
        {
            foreach (var hash in Req.Hashes) {
                FullUpdateQueue.Enqueue(hash.ToByteArray());
            }

            return Task.FromResult(new Empty());
        }
    }
}
