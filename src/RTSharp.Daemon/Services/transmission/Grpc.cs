using BencodeNET.Parsing;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Microsoft.Extensions.Options;

using RTSharp.Daemon.Exceptions;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Services.FileTail;
using RTSharp.Daemon.Utils;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;

using Transmission.Net.Api;
using Transmission.Net.Api.Entity;
using Transmission.Net.Core;
using Transmission.Net.Core.Enums;

namespace RTSharp.Daemon.Services.transmission
{
    public class Grpc
    {
        TransmissionClient Client;
        SessionsService Sessions;
        V1ToV2InfoHashCache Cache;
        ILogger Logger;
        IServiceProvider ServiceProvider;
        string InstanceKey;

        public Grpc([ServiceKey] string InstanceKey, SessionsService Sessions, V1ToV2InfoHashCache Cache, ILogger<Grpc> Logger, IServiceProvider ServiceProvider)
        {
            Client = ServiceProvider.GetRequiredKeyedService<TransmissionClient>(InstanceKey);
            this.InstanceKey = InstanceKey;
            this.ServiceProvider = ServiceProvider;
            this.Sessions = Sessions;
            this.Cache = Cache;
            this.Logger = Logger;
        }

        private readonly ConcurrentInfoHashDictionary<(int Attempts, DateTime NextTry)> ResolveBackoff = new();

        private string? GetTorrentFilePath(string? HashString, string? TorrentFilePath)
        {
            if (!String.IsNullOrEmpty(TorrentFilePath) && System.IO.File.Exists(TorrentFilePath)) {
                return TorrentFilePath;
            } else if (String.IsNullOrEmpty(Client.ConfigDir) || String.IsNullOrEmpty(HashString)) {
                return null;
            } else {
                return Path.Combine(Client.ConfigDir, "torrents", HashString.ToLowerInvariant() + ".torrent");
            }
        }

        private async ValueTask<byte[]> ResolveRTSHash(TorrentView Torrent)
        {
            var v1 = Convert.FromHexString(Torrent.HashString!);
            if (Cache.TryGetRTSHash(v1, out var rtsHash))
                return rtsHash;

            if (Torrent.MetadataPercentComplete < 1)
                return v1;

            var now = DateTime.UtcNow;
            if (ResolveBackoff.TryGetValue(v1, out var b) && now < b.NextTry)
                return v1;

            try {
                var path = GetTorrentFilePath(Torrent.HashString, Torrent.TorrentFile);

                if (path == null)
                    throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, $".torrent not found"));

                var bytes = await System.IO.File.ReadAllBytesAsync(path);
                using var ms = new MemoryStream(bytes);

                var dict = new BencodeParser().Parse<BencodeNET.Objects.BDictionary>(ms);
                var (_, v2) = BencodeUtils.GetInfoHashes(dict);
                Cache.Add(v1, v2);
                ResolveBackoff.TryRemove(v1, out _);
                return v2 ?? v1;
            } catch (Exception ex) {
                Logger.LogTrace(ex, "Failed to resolve rtsHash hash for {Hash}", Torrent.HashString);
                var attempts = ResolveBackoff.TryGetValue(v1, out var bb) ? bb.Attempts + 1 : 1;
                ResolveBackoff[v1] = (attempts, now.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(attempts, 8)))));
                return v1;
            }
        }

        private byte[] ToRTSHash(byte[] V1) => Cache.TryGetRTSHash(V1, out var c) ? c : V1;

        private byte[] ToNative(ReadOnlySpan<byte> RTSHash)
        {
            var v1 = Cache.ToV1(RTSHash) ?? throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, $"Unknown v2 info hash {Convert.ToHexString(RTSHash)}"));
            return v1;
        }

        private ConcurrentInfoHashDictionary<Protocols.DataProvider.Torrent> LatestTorrents = new();

        private ConcurrentInfoHashDictionary<int> HashToId { get; } = new();
        private ConcurrentDictionary<int, byte[]> IdToHash { get; } = new();
        private ConcurrentQueue<byte[]> FullUpdateQueue = new();

        private string ToNativeHashString(ReadOnlySpan<byte> RTSHash) => Convert.ToHexStringLower(ToNative(RTSHash));

        private string[] ToNativeHashStrings(Torrents In)
        {
            var hashes = In.Hashes;
            var ret = new string[hashes.Count];

            for (var x = 0;x < hashes.Count;x++)
                ret[x] = ToNativeHashString(hashes[x].Span);

            return ret;
        }

        private readonly string[] ALL_FIELDS_EXCEPT_LISTS = TorrentFields.ALL_FIELDS.Except([TorrentFields.FILES, TorrentFields.FILE_STATS, TorrentFields.PEERS, TorrentFields.TRACKER_STATS, TorrentFields.TRACKER_LIST, TorrentFields.PIECES]).ToArray();

        public async Task GetTorrentListUpdates(GetTorrentListUpdatesRequest Req, IServerStreamWriter<DeltaTorrentsListResponse> Res, CancellationToken CancellationToken)
        {
            var client = await Client.Init();

            var pollInternal = Req.Interval.ToTimeSpan();

            var delta = new DeltaTorrentsListResponse();

            using var smasScope = ServiceProvider.CreateScope();
            var smas = smasScope.ServiceProvider.GetRequiredService<ISpeedMovingAverageService>();

            void updateHashingSpeed(Protocols.DataProvider.Torrent Torrent)
            {
                if (Torrent.State.HasFlag(Protocols.DataProvider.TorrentState.Hashing)) {
                    var sma = smas.Get(InstanceKey, $"HashingSpeed_{Convert.ToHexString(Torrent.Hash.Span)}");
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

            var all = await client.TorrentGetAsync(null, ALL_FIELDS_EXCEPT_LISTS);

            foreach (var externalTorrent in all!.Torrents) {
                var hash = await ResolveRTSHash(externalTorrent);
                var internalTorrent = new Protocols.DataProvider.Torrent {
                    Hash = hash.ToByteString()
                };
                TorrentMapper.ApplyFromExternal(internalTorrent, externalTorrent);
                updateHashingSpeed(internalTorrent);

                delta.FullUpdate.Add(internalTorrent);
                LatestTorrents[hash] = internalTorrent;
                HashToId[hash] = externalTorrent.Id!.Value;
                IdToHash[externalTorrent.Id!.Value] = hash;
            }

            await Res.WriteAsync(delta, CancellationToken);

            var reportedRemovedIds = new HashSet<int>();

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
                    updateHashingSpeed(internalTorrent);

                    LatestTorrents[Hash] = internalTorrent;
                    HashToId[Hash] = Input.Id!.Value;
                    IdToHash[Input.Id!.Value] = Hash;
                    delta.FullUpdate.Add(internalTorrent);
                }

                List<string>? fullUpdateQueue = null;
                while (FullUpdateQueue.TryDequeue(out var toFullUpdate)) {
                    fullUpdateQueue ??= new();
                    fullUpdateQueue.Add(ToNativeHashString(toFullUpdate));
                }

                Task<TorrentsResult?>? fullUpdateResultTask = null;
                TorrentsResult? list;
                try {
                    if (fullUpdateQueue != null) {
                        fullUpdateResultTask = client.TorrentGetAsync([.. fullUpdateQueue], ALL_FIELDS_EXCEPT_LISTS);
                    }
                    list = await client.TorrentGetRecentyActiveAsync(ALL_FIELDS_EXCEPT_LISTS);
                } catch (Exception ex) when (ex.IsConnectionFailure()) {
                    throw new DataProviderFailure(ex.Message, ex, Client.Uri);
                }

                if (fullUpdateResultTask != null) {
                    TorrentsResult? fullUpdateResult;
                    try {
                        fullUpdateResult = await fullUpdateResultTask;
                    } catch (Exception ex) when (ex.IsConnectionFailure()) {
                        throw new DataProviderFailure(ex.Message, ex, Client.Uri);
                    }
                    list!.Torrents = [.. list!.Torrents, .. fullUpdateResult!.Torrents.ExceptBy(list!.Torrents.Select(x => x.Id), x => x.Id)];
                    if (fullUpdateResult.Removed != null) {
                        list!.Removed ??= [];
                        list!.Removed = [.. list!.Removed, .. fullUpdateResult.Removed.Except(list!.Removed)];
                    }
                }

                foreach (var changedTorrent in list!.Torrents) {
                    var hash = await ResolveRTSHash(changedTorrent);
                    Protocols.DataProvider.Torrent internalTorrent;

                    if (IdToHash.TryGetValue(changedTorrent.Id!.Value, out var previousHash) && !previousHash.AsSpan().SequenceEqual(hash)) {
                        delta.Removed.Add(previousHash.ToByteString());
                        LatestTorrents.TryRemove(previousHash, out _);
                        HashToId.TryRemove(previousHash, out _);
                    }

                    if (!LatestTorrents.TryGetValue(hash, out var torrent)) {
                        fullUpdate(hash, changedTorrent);
                        continue;
                    } else {
                        internalTorrent = torrent;

                        var prevState = internalTorrent.State;
                        var prevSequential = internalTorrent.SequentialDownload;
                        var prevWantedSize = internalTorrent.WantedSize;
                        var prevPriority = internalTorrent.Priority;
                        var prevMagnetDummy = internalTorrent.MagnetDummy;
                        var prevRemotePath = internalTorrent.RemotePath;
                        var prevStatusMessage = internalTorrent.StatusMessage;
                        var prevPrimaryTrackerUri = internalTorrent.PrimaryTracker?.Uri;
                        var prevFinishedOn = internalTorrent.FinishedOn;
                        var labelsChanged = changedTorrent.Labels != null && !internalTorrent.Labels.SequenceEqual(changedTorrent.Labels);

                        TorrentMapper.ApplyFromExternal(internalTorrent, changedTorrent);
                        updateHashingSpeed(internalTorrent);

                        if (internalTorrent.State != prevState ||
                            internalTorrent.SequentialDownload != prevSequential ||
                            internalTorrent.WantedSize != prevWantedSize ||
                            internalTorrent.Priority != prevPriority ||
                            internalTorrent.MagnetDummy != prevMagnetDummy ||
                            internalTorrent.RemotePath != prevRemotePath ||
                            internalTorrent.StatusMessage != prevStatusMessage ||
                            internalTorrent.PrimaryTracker?.Uri != prevPrimaryTrackerUri ||
                            !Equals(internalTorrent.FinishedOn, prevFinishedOn) ||
                            labelsChanged) {
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

                    if (!internalTorrent.IsCompleted) {
                        delta.Incomplete.Add(new IncompleteDeltaTorrentResponse {
                            Hash = internalTorrent.Hash,
                            Downloaded = internalTorrent.Downloaded,
                            CompletedSize = internalTorrent.CompletedSize,
                            Uploaded = internalTorrent.Uploaded,
                            DLSpeed = internalTorrent.DLSpeed,
                            HashingSpeed = internalTorrent.HashingSpeed,
                            UPSpeed = internalTorrent.UPSpeed,
                            ETA = internalTorrent.ETA,
                            SeedersConnected = internalTorrent.SeedersConnected,
                            SeedersTotal = internalTorrent.SeedersTotal,
                            PeersConnected = internalTorrent.PeersConnected,
                            PeersTotal = internalTorrent.PeersTotal,
                            Wasted = internalTorrent.Wasted
                        });
                    } else {
                        delta.Complete.Add(new CompleteDeltaTorrentResponse {
                            Hash = internalTorrent.Hash,
                            Uploaded = internalTorrent.Uploaded,
                            UPSpeed = internalTorrent.UPSpeed,
                            SeedersTotal = internalTorrent.SeedersTotal,
                            PeersTotal = internalTorrent.PeersTotal,
                            PeersConnected = internalTorrent.PeersConnected
                        });
                    }
                }

                if (list.Removed != null) {
                    foreach (var id in list.Removed) {
                        if (!IdToHash.TryGetValue(id, out var hash))
                            continue;

                        // Same torrent was removed and readded
                        if (HashToId.TryGetValue(hash, out var currentId) && currentId != id)
                            continue;

                        // transmission keeps reporting the same removal for ~1 min
                        if (!reportedRemovedIds.Add(id))
                            continue;

                        delta.Removed.Add(hash.ToByteString());
                        LatestTorrents.TryRemove(hash, out _);
                        HashToId.TryRemove(hash, out _);
                    }

                    reportedRemovedIds.IntersectWith(list.Removed);
                }

                if (delta.Incomplete.Count > 0 || delta.Complete.Count > 0 || delta.Removed.Count > 0 || delta.FullUpdate.Count > 0)
                    await Res.WriteAsync(delta, CancellationToken);
                await Task.Delay(pollInternal, CancellationToken);
            }
        }

        public async Task<TorrentsListResponse> GetTorrentList()
        {
            var client = await Client.Init();

            var all = await client.TorrentGetAsync(null, TorrentFields.ALL_FIELDS);

            var ret = new TorrentsListResponse();
            foreach (var x in all!.Torrents) {
                var internalTorrent = new Protocols.DataProvider.Torrent {
                    Hash = (await ResolveRTSHash(x)).ToByteString()
                };
                TorrentMapper.ApplyFromExternal(internalTorrent, x);
                ret.List.Add(internalTorrent);
            }
            return ret;
        }

        public async Task<TorrentsReply> PerformVoidActionObj(Torrents In, Func<ITransmissionClient, object[], Task> Fx)
        {
            var client = await Client.Init();

            var tasks = new List<Task<TorrentsReply.Types.TorrentReply>>();
            Exception? exception = null;

            try {
                await Fx(client, ToNativeHashStrings(In));
            } catch (Exception ex) {
                exception = ex;
            }

            return new TorrentsReply {
                Torrents = {
                    In.Hashes.Select(x => new TorrentsReply.Types.TorrentReply {
                        InfoHash = x,
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

        public Task<TorrentsReply> StartTorrents(Torrents In) => PerformVoidActionObj(In, (client, obj) => client.TorrentStartAsync(obj));

        public Task<TorrentsReply> PauseTorrents(Torrents In) => throw new NotSupportedException();

        public Task<TorrentsReply> StopTorrents(Torrents In) => PerformVoidActionObj(In, (client, obj) => client.TorrentStopAsync(obj));

        public async Task ForceRecheckTorrents(Torrents In, ScriptProgressState Progress, CancellationToken CancellationToken)
        {
            var (res, hashes) = await SendForceRecheck(In);

            await PollForceRecheck(res, hashes, Progress, CancellationToken);
        }

        private async Task<((byte[] Hash, IList<Exception> Exceptions)[] Res, byte[][] Hashes)> SendForceRecheck(Torrents In)
        {
            var client = await Client.Init();

            Exception? exception = null;

            try {
                await client.TorrentVerifyAsync(ToNativeHashStrings(In));
            } catch (Exception ex) {
                exception = ex;
            }

            Logger.LogDebug("Recheck sent");

            var res = In.Hashes.Select(x => (Hash: x.ToByteArray(), Exceptions: exception == null ? (IList<Exception>)Array.Empty<Exception>() : ([exception]))).ToArray();

            var hashes = In.Hashes.Select(x => x.ToByteArray()).ToArray();

            return (res, hashes);
        }

        private async Task PollForceRecheck((byte[] Hash, IList<Exception> Exceptions)[] res, byte[][] hashes, ScriptProgressState Progress, CancellationToken CancellationToken)
        {
            var client = await Client.Init();

            Progress.Progress = 0f;
            Progress.State = TASK_STATE.RUNNING;

            var sw = Stopwatch.StartNew();

            var stateObserved = new InfoHashDictionary<TorrentStatus?>();
            var stateObserved2 = new InfoHashDictionary<TorrentStatus?>();
            foreach (var torrentResult in res) {
                stateObserved[torrentResult.Hash] = null;
                stateObserved2[torrentResult.Hash] = null;
            }

            while (true) {
                if (CancellationToken.IsCancellationRequested)
                    break;

                Logger.LogDebug("Getting partial data inside session");

                var partialData = await client.TorrentGetRecentyActiveAsync(TorrentFields.HASH_STRING, TorrentFields.STATUS);

                Logger.LogDebug("Got partial data inside session");

                foreach (var hash in hashes) {
                    var hashStr = Convert.ToHexStringLower(ToNative(hash));

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
                        Progress.State = TASK_STATE.DONE;
                        break;
                    }
                } else {
                    Logger.LogWarning("Finishing checking for rehash state after 10 minutes");
                    Progress.State = TASK_STATE.FAILED;
                    break;
                }

                await Task.Delay(1000); // TODO: Customizable?
            }

            Logger.LogDebug("Session complete");
        }

        public Task<TorrentsReply> ReannounceToAllTrackers(Torrents In) => PerformVoidActionObj(In, (client, obj) => client.TorrentReannounceAsync(obj));

        private async Task<bool> TryWriteFastResume(BencodeNET.Objects.BDictionary BTorrent, byte[] InfoHash, string BaseDirectory)
        {
            if (String.IsNullOrEmpty(BaseDirectory) || String.IsNullOrEmpty(Client.ConfigDir))
                return false;

            if (!await BencodeUtils.FastVerifyFilesOnDisk(BTorrent, BaseDirectory, Logger))
                return false;

            var info = BTorrent.Get<BencodeNET.Objects.BDictionary>(BencodeNET.Torrents.TorrentFields.Info);

            var mtimes = new BencodeNET.Objects.BList();
            foreach (var file in BencodeUtils.GetFilesOnDisk(info, BaseDirectory)) {
                mtimes.Add((BencodeNET.Objects.IBObject)new BencodeNET.Objects.BNumber(
                    new DateTimeOffset(
                        System.IO.File.GetLastWriteTimeUtc(file.Path)
                    ).ToUnixTimeSeconds()
                ));
            }

            var resume = new BencodeNET.Objects.BDictionary {
                { "destination", new BencodeNET.Objects.BString(BaseDirectory) },
                { "progress", new BencodeNET.Objects.BDictionary {
                    { "pieces", new BencodeNET.Objects.BString("all") },
                    { "blocks", new BencodeNET.Objects.BString("all") },
                    { "mtimes", mtimes }
                } }
            };

            var resumeDir = Path.Combine(Client.ConfigDir, "resume");
            var resumePath = Path.Combine(resumeDir, Convert.ToHexStringLower(InfoHash) + ".resume");

            try {
                Directory.CreateDirectory(resumeDir);
                System.IO.File.WriteAllBytes(resumePath, BencodeNET.Objects.BObjectExtensions.EncodeAsBytes(resume));
            } catch (Exception ex) {
                Logger.LogWarning(ex, "Fast resume: failed to write resume file at {path}", resumePath);
                return false;
            }

            Logger.LogDebug($"Fast resume: wrote {resumePath} base dir: {BaseDirectory}");

            return true;
        }

        record Torrent(string Path, string Filename, MemoryStream Data, bool FastResume, bool Magnet);

        public async Task<TorrentsReply> AddTorrents(IAsyncEnumerable<NewTorrentsData> Req)
        {
            var client = await Client.Init();

            var torrents = new Dictionary<string, Torrent>();
            await foreach (var chunk in Req) {
                switch (chunk.DataCase) {
                    case NewTorrentsData.DataOneofCase.TMetadata:
                        torrents[chunk.TMetadata.Id] = new Torrent(chunk.TMetadata.Path, chunk.TMetadata.Filename, new MemoryStream(), chunk.TMetadata.FastResume, chunk.TMetadata.Magnet);
                        break;
                    case NewTorrentsData.DataOneofCase.TData:
                        torrents[chunk.TData.Id].Data.Write(chunk.TData.Chunk.ToByteArray());
                        break;
                }
            }

            var bencodeParser = new BencodeParser();
            var ret = new TorrentsReply();

            foreach (var (id, torrent) in torrents) {
                torrent.Data.Position = 0;

                BencodeNET.Objects.BDictionary? bTorrent = null;
                Uri? magnetUri = null;
                byte[]? v1;
                byte[]? v2;
                if (torrent.Magnet) {
                    if (!MagnetParser.TryParse(Encoding.UTF8.GetString(torrent.Data.ToArray()), out var magnet)) {
                        ret.Torrents.Add(new TorrentsReply.Types.TorrentReply() {
                            InfoHash = Array.Empty<byte>().ToByteString(),
                            Status = { new Protocols.DataProvider.Status() {
                                Command = "",
                                FaultCode = "1",
                                FaultString = $"Invalid magnet URI on torrent id {id}"
                            } }
                        });
                        continue;
                    }

                    magnetUri = magnet.Uri;
                    (v1, v2) = (magnet.V1InfoHash, magnet.V2InfoHash);
                } else {
                    try {
                        bTorrent = bencodeParser.Parse<BencodeNET.Objects.BDictionary>(torrent.Data);
                        (v1, v2) = BencodeUtils.GetInfoHashes(bTorrent);
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
                }

                if (v1 == null) {
                    ret.Torrents.Add(new TorrentsReply.Types.TorrentReply() {
                        InfoHash = Array.Empty<byte>().ToByteString(),
                        Status = { new Protocols.DataProvider.Status() {
                            Command = "",
                            FaultCode = "1",
                            FaultString = $"transmission does not support v2-only torrents (id {id})"
                        } }
                    });
                    continue;
                }

                byte[] rtsHash;
                if ((!torrent.Magnet || v2 != null) && Cache.Add(v1, v2))
                    rtsHash = v2 ?? v1;

                else if (!Cache.TryGetRTSHash(v1, out rtsHash))
                    rtsHash = v1;

                try {
                    if (magnetUri == null) {
                        if (torrent.FastResume) {
                            if (!await TryWriteFastResume(bTorrent!, v1, torrent.Path))
                                Logger.LogWarning($"Fast resume requested but could not be applied for {Convert.ToHexString(v1)}");
                        }

                        var b64 = Convert.ToBase64String(torrent.Data.ToArray());

                        await client.TorrentAddAsync(new NewTorrent {
                            DownloadDirectory = torrent.Path,
                            Metainfo = b64,
                            Paused = true
                        });
                    } else {
                        Logger.LogInformation($"{InstanceKey}: Adding magnet {Convert.ToHexString(v1)}");

                        await client.TorrentAddAsync(new NewTorrent {
                            DownloadDirectory = torrent.Path,
                            Filename = magnetUri.OriginalString,
                            Paused = true
                        });
                    }
                } catch (Exception) {
                    ret.Torrents.Add(new TorrentsReply.Types.TorrentReply() {
                        InfoHash = Array.Empty<byte>().ToByteString(),
                        Status = { new Protocols.DataProvider.Status() {
                            Command = "",
                            FaultCode = "1",
                            FaultString = $"Failed to add torrent id {id}"
                        } }
                    });
                    continue;
                }

                ret.Torrents.Add(new TorrentsReply.Types.TorrentReply() {
                    InfoHash = rtsHash.ToByteString(),
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

        private async Task<TorrentsFilesReply.Types.TorrentsFiles> GetTorrentFiles(byte[] Hash)
        {
            var client = await Client.Init();

            // Could also fetch from torrent polling task if its live
            var torrents = (await client.TorrentGetAsync([ ToNativeHashString(Hash) ], [ TorrentFields.FILES, TorrentFields.HASH_STRING, TorrentFields.NAME, TorrentFields.FILE_STATS, TorrentFields.PIECE_SIZE ]))!.Torrents;

            if (torrents.Length == 0) {
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.InvalidArgument, "No such torrent exists"));
            }

            var x = torrents[0];

            var files = x.Files ?? [];

            return new TorrentsFilesReply.Types.TorrentsFiles {
                InfoHash = ToRTSHash(Convert.FromHexString(x.HashString!)).ToByteString(),
                MultiFile = files.Length != 0 && files[0].Name!.Contains('/'),
                Files = {
                    files.Select((i, idx) => {
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
            };
        }

        public async Task<TorrentsFilesReply> GetTorrentsFiles(Torrents Req)
        {
            var tasks = Req.Hashes.Select(x => GetTorrentFiles(x.ToByteArray())).ToArray();

            await Task.WhenAll(tasks);

            return new TorrentsFilesReply {
                Reply = { tasks.Select(x => x.Result) }
            };
        }

        public Task GetTorrentFileUpdates(GetTorrentFileUpdatesRequest Req, IServerStreamWriter<DeltaTorrentsFilesResponse> Res, CancellationToken CancellationToken)
        {
            var hash = Req.InfoHash.ToByteArray();

            return TorrentFileUpdatesStreamer.Stream(_ => GetTorrentFiles(hash), Req.Interval.ToTimeSpan(), Res, CancellationToken);
        }

        public Task<TorrentsReply> RemoveTorrents(Torrents In) => PerformVoidActionObj(In, (client, x) => client.TorrentRemoveAsync(x, false));

        public Task<TorrentsReply> RemoveTorrentsAndData(Torrents In) => PerformVoidActionObj(In, (client, x) => client.TorrentRemoveAsync(x, true));

        public async Task<Stream> GetDotTorrentStream(ByteString Hash, CancellationToken CancellationToken)
        {
            var client = await Client.Init();

            var torrent = await client.TorrentGetAsync([ ToNativeHashString(Hash.Span) ], TorrentFields.TORRENT_FILE);

            if (torrent?.Torrents?.Any() != true) {
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, "Torrent not found"));
            }

            var file = GetTorrentFilePath(torrent.Torrents[0].HashString, torrent.Torrents[0].TorrentFile);

            if (file == null || !System.IO.File.Exists(file))
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, $".torrent file doesn't exist"));

            return System.IO.File.OpenRead(torrent.Torrents[0].TorrentFile!);
        }

        public async Task GetDotTorrents(Torrents Req, IServerStreamWriter<DotTorrentsData> Res, CancellationToken CancellationToken)
        {
            var client = await Client.Init();

            var torrents = await client.TorrentGetAsync(ToNativeHashStrings(Req), [ TorrentFields.HASH_STRING, TorrentFields.TORRENT_FILE ]);

            for (var x = 0;x < torrents!.Torrents.Length;x++) {
                var hash = await ResolveRTSHash(torrents!.Torrents[x]);
                var idx = x.ToString();

                await Res.WriteAsync(new DotTorrentsData {
                    TMetadata = new DotTorrentsData.Types.Metadata {
                        Id = idx,
                        Hash = hash.ToByteString()
                    }
                }, CancellationToken);

                try {
                    var file = GetTorrentFilePath(torrents.Torrents[x].HashString, torrents.Torrents[x].TorrentFile);

                    if (file == null || !System.IO.File.Exists(file))
                        throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, $".torrent file doesn't exist"));

                    var stm = System.IO.File.OpenRead(file!);

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
                    Logger.LogError(ex, $"Failed to get .torrent for {Convert.ToHexString(hash)}");

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
            var client = await Client.Init();

            var torrents = await client.TorrentGetAsync(ToNativeHashStrings(Req), [TorrentFields.HASH_STRING, TorrentFields.PEERS, TorrentFields.TOTAL_SIZE]);

            return new TorrentsPeersReply {
                Reply = {
                    torrents!.Torrents.Select(x => {
                        using var scope = ServiceProvider.CreateScope();
                        var smas = scope.ServiceProvider.GetRequiredService<ISpeedMovingAverageService>();

                        return new TorrentsPeersReply.Types.TorrentsPeers {
                            InfoHash = ToRTSHash(Convert.FromHexString(x.HashString!)).ToByteString(),
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
            var client = await Client.Init();

            var torrents = await client.TorrentGetAsync(ToNativeHashStrings(Req), [TorrentFields.HASH_STRING, TorrentFields.TRACKER_STATS]);

            return new TorrentsTrackersReply {
                Reply = {
                    await Task.WhenAll(torrents!.Torrents.Select(async x => {
                        var trackers = x.TrackerStats;

                        return new TorrentsTrackersReply.Types.TorrentsTrackers {
                            InfoHash = ToRTSHash(Convert.FromHexString(x.HashString!)).ToByteString(),
                            Trackers = {
                                trackers!.Select(x => new Protocols.DataProvider.TorrentTracker {
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
            var client = await Client.Init();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

            if (!Req.DeleteSourceFiles) {
                throw new ArgumentException("DeleteSourceFiles = false is not supported");
            }

            async Task fx(ScriptSession session)
            {
                session.Progress.State = TASK_STATE.RUNNING;
                session.Progress.Progress = 0f;

                var sessLock = new Lock();
                var totalCount = Req.Torrents.Count;
                var completedCount = 0;

                var tasks = new List<Task<(ByteString Hash, IList<Exception> Exceptions)>>();
                foreach (var req in Req.Torrents) {
                    tasks.Add(Task.Run(async () => {
                        Exception? exception = null;

                        ScriptProgressState progress;
                        lock (sessLock) {
                            session.Progress.Chain =
                            [
                                ..(session.Progress.Chain ?? []),
                                progress = new ScriptProgressState(session) {
                                    Text = $"Setting location for {Convert.ToHexString(req.InfoHash.Span)} to {req.TargetDirectory}",
                                    Progress = 0f,
                                    State = TASK_STATE.RUNNING
                                },
                            ];
                        }

                        try {
                            await client.TorrentSetLocationAsync([ToNativeHashString(req.InfoHash.Span)], req.TargetDirectory, Req.Move);
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
                        progress.State = TASK_STATE.DONE;
                        session.Progress.Progress = Interlocked.Increment(ref completedCount) / (float)totalCount * 100;

                        return (req.InfoHash, exception == null ? (IList<Exception>)Array.Empty<Exception>() : [exception]);
                    }));
                }

                await Task.WhenAll(tasks);

                session.Progress.Progress = 100f;
                session.Progress.State = TASK_STATE.DONE;
                session.Progress.StateData = "";
                session.Progress.Text = "Done";
            }

            var session = Sessions.RunSession("Move download directory", cts, fx);

            return session.Id.ToByteArray().ToBytesValue();
        }

        public async Task<TorrentsReply> SetLabels(SetLabelsArgs Req)
        {
            var client = await Client.Init();

            Logger.LogInformation("In SetLabels");

            var tasks = new List<Task<IEnumerable<TorrentsReply.Types.TorrentReply>>>();

            foreach (var pair in Req.In.GroupBy(x => [.. x.Labels], new StringArrayComparer())) {
                var hashes = pair.Select(x => x.InfoHash.ToByteArray());
                var labels = pair.Key;
                async Task<IEnumerable<TorrentsReply.Types.TorrentReply>> setLabels(IEnumerable<byte[]> Hashes, string[] Labels)
                {
                    Exception? exception = null;

                    try {
                        await client.TorrentSetAsync(new Transmission.Net.Arguments.TorrentSettings {
                            IDs = Hashes.Select(x => ToNativeHashString(x)).ToArray(),
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
            var client = await Client.Init();

            var torrents = await client.TorrentGetAsync(ToNativeHashStrings(Req), [ TorrentFields.HASH_STRING, TorrentFields.PIECES ]);

            return new TorrentsPiecesReply {
                Reply = {
                    torrents!.Torrents.Select(x => {
                        var pieces = Convert.FromBase64String(x.Pieces!);

                        return new TorrentsPiecesReply.Types.TorrentsPieces {
                            InfoHash = ToRTSHash(Convert.FromHexString(x.HashString!)).ToByteString(),
                            Bitfield = pieces.Length == 0 ? ByteString.CopyFrom(0) : ByteString.CopyFrom(pieces)
                        };
                    })
                }
            };
        }

        public async Task<Protocols.DataProvider.Torrent> GetTorrent(BytesValue Hash)
        {
            var client = await Client.Init();

            var torrent = await client.TorrentGetAsync(new object[] { ToNativeHashString(Hash.Value.Span) });

            if (!torrent!.Torrents.Any())
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, "Torrent not found"));

            var internalTorrent = new Protocols.DataProvider.Torrent {
                Hash = ByteString.CopyFrom(Hash.Value.Span)
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

        public async Task<Empty> EditTracker(ByteString InfoHash, string Existing, string New, CancellationToken CancellationToken)
        {
            var client = await Client.Init();

            var hash = ToNativeHashString(InfoHash.Span);

            var torrents = await client.TorrentGetAsync([ hash ], [ TorrentFields.TRACKER_LIST ]);
            var torrent = torrents?.Torrents.SingleOrDefault();

            if (torrent == default) {
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, "Torrent not found"));
            }

            var newTrackerList = new List<string>();

            foreach (var tracker in torrent.TrackerList!.Split([ "\r\n", "\n" ], StringSplitOptions.None)) {
                if (tracker == Existing) {
                    newTrackerList.Add(New);
                } else {
                    newTrackerList.Add(tracker);
                }
            }

            await client.TorrentSetAsync(new Transmission.Net.Arguments.TorrentSettings {
                IDs = new[] { hash },
                TrackerList = string.Join("\r\n", newTrackerList)
            });

            return new();
        }

        public async Task<Empty> AddTracker(ByteString InfoHash, string Tracker, CancellationToken CancellationToken)
        {
            var client = await Client.Init();

            var hash = ToNativeHashString(InfoHash.Span);

            var torrents = await client.TorrentGetAsync([ hash ], [ TorrentFields.TRACKER_LIST ]);
            var torrent = torrents?.Torrents.SingleOrDefault();

            if (torrent == default) {
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, "Torrent not found"));
            }

            var newTrackerList = new List<string>(torrent.TrackerList!.Split([ "\r\n", "\n" ], StringSplitOptions.None)) {
                Tracker
            };

            await client.TorrentSetAsync(new Transmission.Net.Arguments.TorrentSettings {
                IDs = new[] { hash },
                TrackerList = string.Join("\r\n", newTrackerList)
            });

            return new();
        }

        public async Task<Empty> RemoveTracker(ByteString InfoHash, string Tracker, CancellationToken CancellationToken)
        {
            var client = await Client.Init();

            var hash = ToNativeHashString(InfoHash.Span);

            var torrents = await client.TorrentGetAsync([ hash ], [TorrentFields.TRACKER_LIST]);
            var torrent = torrents?.Torrents.SingleOrDefault();

            if (torrent == default) {
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, "Torrent not found"));
            }

            var newTrackerList = new List<string>(torrent.TrackerList!.Split(["\r\n", "\n"], StringSplitOptions.None));

            if (newTrackerList.RemoveAll(x => x == Tracker) == 0) {
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, "Tracker not found"));
            }

            await client.TorrentSetAsync(new Transmission.Net.Arguments.TorrentSettings {
                IDs = new[] { hash },
                TrackerList = string.Join("\r\n", newTrackerList)
            });

            return new();
        }

        public async Task<Empty> SetSequentialDownload(IEnumerable<ByteString> InfoHashes, bool Enabled, CancellationToken cancellationToken)
        {
            var client = await Client.Init();

            await client.TorrentSetAsync(new Transmission.Net.Arguments.TorrentSettings {
                IDs = InfoHashes.Select(x => ToNativeHashString(x.Span)).ToArray(),
                SequentialDownload = Enabled
            });

            return new();
        }

        public async Task<Empty> SetFilePriority(ByteString InfoHash, IEnumerable<uint> Indexes, TorrentsFilesReply.Types.FilePriority Priority, CancellationToken cancellationToken)
        {
            var client = await Client.Init();

            var indexes = Indexes.Select(x => (int)x).ToArray();

            var settings = new Transmission.Net.Arguments.TorrentSettings {
                IDs = new[] { ToNativeHashString(InfoHash.Span) }
            };

            switch (Priority) {
                case TorrentsFilesReply.Types.FilePriority.DontDownload:
                    settings.FilesUnwanted = indexes;
                    break;
                case TorrentsFilesReply.Types.FilePriority.Normal:
                    settings.FilesWanted = indexes;
                    settings.PriorityNormal = indexes;
                    break;
                case TorrentsFilesReply.Types.FilePriority.High:
                    settings.FilesWanted = indexes;
                    settings.PriorityHigh = indexes;
                    break;
                default:
                    throw new RpcException(new global::Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown file priority"));
            }

            await client.TorrentSetAsync(settings);

            return new();
        }

        public async Task<Protocols.DataProvider.AllTimeDataStats> GetAllTimeDataStats(CancellationToken cancellationToken)
        {
            var client = await Client.Init();

            var stats = await client.GetSessionStatisticAsync();

            return new Protocols.DataProvider.AllTimeDataStats {
                Download = (ulong)stats!.CumulativeStats!.DownloadedBytes!,
                Upload = (ulong)stats.CumulativeStats.UploadedBytes!,
                ShareRatio = (float)stats.CumulativeStats.DownloadedBytes.Value / (float)stats.CumulativeStats.UploadedBytes.Value
            };
        }

        public Task StreamLogs(StreamLogsRequest Req, IServerStreamWriter<LogEntry> Res, CancellationToken CancellationToken)
        {
            var config = ServiceProvider.GetRequiredService<IOptionsFactory<ConfigModel>>().Create(InstanceKey);

            if (String.IsNullOrWhiteSpace(config.LogFile)) {
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.FailedPrecondition, "No log file configured for this transmission data provider (set LogFile; run transmission-daemon with --logfile)."));
            }

            return FileTailReader.StreamPath(config.LogFile, Req, Res, CancellationToken);
        }
    }
}
