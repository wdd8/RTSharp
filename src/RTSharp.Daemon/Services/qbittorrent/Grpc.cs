using BencodeNET.Parsing;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using QBittorrent.Client;

using RTSharp.Daemon.Exceptions;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Utils;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

using System.Diagnostics;
using System.Text;
using System.Web;

namespace RTSharp.Daemon.Services.qbittorrent
{
    public class Grpc
    {
        QbitClient Client;
        SessionsService Sessions;
        ILogger Logger;
        IServiceProvider ServiceProvider;
        string InstanceKey;

        public Grpc([ServiceKey] string InstanceKey, SessionsService Sessions, ILogger<Grpc> Logger, IServiceProvider ServiceProvider)
        {
            Client = ServiceProvider.GetRequiredKeyedService<QbitClient>(InstanceKey);
            this.InstanceKey = InstanceKey;
            this.ServiceProvider = ServiceProvider;
            this.Sessions = Sessions;
            this.Logger = Logger;
        }

        // Keyed by native hash
        private InfoHashDictionary<Protocols.DataProvider.Torrent> LatestTorrents = new();
        private ReaderWriterLockSlim LatestTorrentsLock = new();

        private static string ToNativeHex(ReadOnlySpan<byte> RTSHash) => Convert.ToHexStringLower(RTSHash[..InfoHash.V1Length]);

        private static byte[] RTSHashFromInfo(string? V1Hex, string? V2Hex, string NativeHex)
        {
            if (!String.IsNullOrEmpty(V2Hex))
                return Convert.FromHexString(V2Hex);

            if (!String.IsNullOrEmpty(V1Hex))
                return Convert.FromHexString(V1Hex);

            return Convert.FromHexString(NativeHex);
        }

        public string PathCombineFSlash(string a, string b)
        {
            return Path.Combine(a, b).Replace("\\", "/");
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

            var bencodeParser = new BencodeNET.Parsing.BencodeParser();
            var ret = new TorrentsReply();
            var unaccountedNative = new HashSet<string>();

            foreach (var (id, torrent) in torrents) {
                torrent.Data.Position = 0;

                byte[] torrentId;
                byte[] rtsHash;
                BencodeNET.Objects.BDictionary? bTorrent = null;
                Uri? magnetUri = null;

                if (!torrent.Magnet) {
                    try {
                        bTorrent = bencodeParser.Parse<BencodeNET.Objects.BDictionary>(torrent.Data);
                        var (v1, v2) = BencodeUtils.GetInfoHashes(bTorrent);
                        // qbittorrent identifies torrents by the truncated v2 info hash when one exists
                        torrentId = v2 != null ? InfoHash.TruncateV2(v2) : v1!;
                        // RTSHash is the full v2 for hybrids/v2, else v1.
                        rtsHash = (v2 ?? v1)!;
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
                } else {
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

                    torrentId = magnet.V2InfoHash != null ? InfoHash.TruncateV2(magnet.V2InfoHash) : magnet.V1InfoHash!;
                    magnetUri = magnet.Uri;
                    rtsHash = (magnet.V2InfoHash ?? magnet.V1InfoHash)!;
                }

                try {
                    if (bTorrent != null) {
                        var skipHashChecking = false;
                        if (torrent.FastResume) {
                            skipHashChecking = await BencodeUtils.FastVerifyFilesOnDisk(bTorrent, torrent.Path, Logger);
                            if (!skipHashChecking)
                                Logger.LogWarning("Fast resume requested but could not be applied for {hash}, adding normally", Convert.ToHexString(torrentId));
                        }

                        var path = Path.GetTempFileName();
                        await System.IO.File.WriteAllBytesAsync(path, torrent.Data.ToArray());

                        Logger.LogInformation($"{InstanceKey}: Adding torrent {Convert.ToHexString(torrentId)}");

                        await client.AddTorrentsAsync(new AddTorrentFilesRequest {
                            DownloadFolder = torrent.Path,
                            Paused = true,
                            SkipHashChecking = skipHashChecking,
                            TorrentFiles = { path }
                        });

                        System.IO.File.Delete(path);
                    } else if (magnetUri != null) {
                        Logger.LogInformation($"{InstanceKey}: Adding magnet {Convert.ToHexString(torrentId)}");

                        await client.AddTorrentsAsync(new AddTorrentUrlsRequest {
                            DownloadFolder = torrent.Path,
                            Paused = true,
                            SkipHashChecking = false,
                            TorrentUrls = { magnetUri }
                        });
                    }

                    unaccountedNative.Add(Convert.ToHexStringLower(torrentId));

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
            }

            if (unaccountedNative.Count > 0) {
                Logger.LogInformation($"{InstanceKey}: Looking for torrents {String.Join(", ", unaccountedNative)}");

                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < 30) {
                    var list = await client.GetTorrentListAsync(new TorrentListQuery { Hashes = [.. unaccountedNative] });

                    foreach (var torrent in list) {
                        if (unaccountedNative.Remove(torrent.Hash))
                            Logger.LogInformation($"{InstanceKey}: Torrent {torrent.Hash}");
                    }

                    if (unaccountedNative.Count == 0)
                        break;

                    await Task.Delay(1000); // needs cancellation token
                }
            }

            return ret;
        }

        public async Task ForceRecheckTorrents(Torrents Req, ScriptProgressState Progress, CancellationToken CancellationToken)
        {
            var (res, hashes) = await SendForceRecheck(Req);

            await PollForceRecheck(res, hashes, Progress, CancellationToken);
        }

        private async Task<((byte[] Hash, IList<Exception> Exceptions)[] Res, byte[][] Hashes)> SendForceRecheck(Torrents Req)
        {
            var client = await Client.Init();

            Exception? exception = null;

            try {
                await client.RecheckAsync(Req.Hashes.Select(x => ToNativeHex(x.Span)));
            } catch (Exception ex) {
                exception = ex;
            }

            Logger.LogInformation("Recheck sent");

            var res = Req.Hashes.Select(x => (Hash: x.ToByteArray(), Exceptions: exception == null ? (IList<Exception>)Array.Empty<Exception>() : ([ exception ]))).ToArray();

            var hashes = Req.Hashes.Select(x => x.ToByteArray()).ToArray();

            return (res, hashes);
        }

        private async Task PollForceRecheck((byte[] Hash, IList<Exception> Exceptions)[] res, byte[][] hashes, ScriptProgressState Progress, CancellationToken CancellationToken)
        {
            var client = await Client.Init();

            Progress.Progress = 0f;
            Progress.State = TASK_STATE.RUNNING;

            var sw = Stopwatch.StartNew();

            var stateObserved = new InfoHashDictionary<QBittorrent.Client.TorrentState>();
            var stateObserved2 = new InfoHashDictionary<QBittorrent.Client.TorrentState>();
            var progressObserved = new InfoHashDictionary<double>();
            var progressLastMoved = new InfoHashDictionary<TimeSpan>();
            foreach (var torrentResult in res) {
                stateObserved[torrentResult.Hash] = QBittorrent.Client.TorrentState.Unknown;
                stateObserved2[torrentResult.Hash] = QBittorrent.Client.TorrentState.Unknown;
            }

            var hashStrs = hashes.Select(x => ToNativeHex(x)).ToArray();

            try {
                while (true) {
                    if (CancellationToken.IsCancellationRequested)
                        break;

                    Logger.LogDebug("Getting torrent list");

                    var list = await client.GetTorrentListAsync(new TorrentListQuery { Hashes = hashStrs }, CancellationToken);
                    var byHash = list.ToDictionary(x => x.Hash);

                    foreach (var hash in hashes) {
                        // Compare against qbittorrent's native hash; stateObserved dicts stay keyed by rtsHash.
                        var hashStr = ToNativeHex(hash);

                        if (!byHash.TryGetValue(hashStr, out var torrentInfo)) {
                            Logger.LogDebug($"Torrent {hashStr} not in list");
                            continue;
                        }

                        Logger.LogDebug($"Torrent {hashStr} state: {torrentInfo.State}");

                        var currentValue =
                            torrentInfo.State == QBittorrent.Client.TorrentState.CheckingDownload ||
                            torrentInfo.State == QBittorrent.Client.TorrentState.CheckingUpload ||
                            torrentInfo.State == QBittorrent.Client.TorrentState.QueuedForChecking ? QBittorrent.Client.TorrentState.CheckingDownload : torrentInfo.State;

                        if (stateObserved2[hash] != currentValue) {
                            stateObserved[hash] = stateObserved2[hash];
                            stateObserved2[hash] = currentValue;

                            Logger.LogDebug($"State transition for {hashStr}: {stateObserved[hash]} -> {stateObserved2[hash]}");
                        }

                        var currentProgress = torrentInfo.Progress;

                        if (currentValue == QBittorrent.Client.TorrentState.CheckingDownload && progressObserved.TryGetValue(hash, out var previousProgress) && previousProgress != currentProgress) {
                            progressLastMoved[hash] = sw.Elapsed;
                        }

                        progressObserved[hash] = currentProgress;
                    }

                    if (sw.Elapsed < TimeSpan.FromSeconds(5)) {
                        // Torrents at this point are seeding (did not enter hashing state), hashing, or completed hashing

                        bool pass = true;
                        foreach (var hash in hashes) {
                            var s = stateObserved[hash];
                            var s2 = stateObserved2[hash];

                            // Can't retrieve
                            if (s == QBittorrent.Client.TorrentState.Unknown && s2 == QBittorrent.Client.TorrentState.Unknown) {
                                Logger.LogDebug($"(2) Cannot retrieve state for {Convert.ToHexStringLower(hash)}");
                                continue;
                            }

                            // Only one state transition
                            if (s == QBittorrent.Client.TorrentState.Unknown && s2 != QBittorrent.Client.TorrentState.Unknown) {
                                Logger.LogDebug($"(2) {Convert.ToHexStringLower(hash)} only one state transision");
                                pass = false;
                                break;
                            }

                            // Hashing
                            if (s2 == QBittorrent.Client.TorrentState.CheckingDownload) {
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
                    } else {
                        bool pass = true;
                        foreach (var hash in hashes) {
                            var s = stateObserved[hash];
                            var s2 = stateObserved2[hash];

                            // Can't retrieve
                            if (s == QBittorrent.Client.TorrentState.Unknown && s2 == QBittorrent.Client.TorrentState.Unknown) {
                                Logger.LogDebug($"(1) Cannot retrieve state for {Convert.ToHexStringLower(hash)}");
                                continue;
                            }

                            // Still hashing
                            if (s2 == QBittorrent.Client.TorrentState.CheckingDownload) {
                                if (progressLastMoved.TryGetValue(hash, out var lastMoved) &&
                                    sw.Elapsed - lastMoved > TimeSpan.FromMinutes(1)) {

                                    Logger.LogDebug($"(1) {Convert.ToHexStringLower(hash)} done% stuck for more than a minute");
                                    Progress.State = TASK_STATE.FAILED;
                                    break;
                                }

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
                    }

                    await Task.Delay(1000); // TODO: Customizable?
                }
            } finally {
                Progress.State = TASK_STATE.DONE;
            }
        }

        public async Task<Stream> GetDotTorrentStream(ByteString Hash, CancellationToken CancellationToken)
        {
            var client = await Client.Init();
            return await client.GetTorrentFileAsync(ToNativeHex(Hash.Span), CancellationToken);
        }

        public async Task GetDotTorrents(Torrents Req, IServerStreamWriter<DotTorrentsData> Res, CancellationToken CancellationToken)
        {
            var client = await Client.Init();

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
                    var stm = await client.GetTorrentFileAsync(ToNativeHex(hash.Span), CancellationToken);

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

        private async Task<TorrentsFilesReply.Types.TorrentsFiles> GetTorrentFiles(byte[] Hash)
        {
            var client = await Client.Init();

            var contents = await client.GetTorrentContentsAsync(ToNativeHex(Hash), null);

            Protocols.DataProvider.Torrent torrent;
            LatestTorrentsLock.EnterReadLock();
            try {
                if (!LatestTorrents.TryGetValue(InfoHash.TruncateV2(Hash), out torrent)) {
                    throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, "Torrent not found"));
                }
            } finally {
                LatestTorrentsLock.ExitReadLock();
            }

            return new TorrentsFilesReply.Types.TorrentsFiles {
                InfoHash = Hash.ToByteString(),
                MultiFile = contents != null && contents.Count != 0 && contents[0].Name.Contains('/'),
                Files = {
                    contents == null || contents.Count == 0 ? [] : contents.Select(i => {
                        var torrentName = torrent.Name;

                        i.Name = i.Name.StartsWith(torrentName + '/') ? i.Name[(torrentName.Length + 1)..] : i.Name;
                        return new TorrentsFilesReply.Types.File {
                            Path = i.Name,
                            Size = (ulong)i.Size,
                            DownloadedPieces = (ulong)(i.PieceRange.EndIndex - i.PieceRange.StartIndex * i.Progress),
                            Downloaded = (ulong)(i.Progress * i.Size),
                            Priority = i.Priority switch {
                                TorrentContentPriority.Skip => TorrentsFilesReply.Types.FilePriority.DontDownload,
                                TorrentContentPriority.Minimal => TorrentsFilesReply.Types.FilePriority.Normal, // TODO: missing
                                TorrentContentPriority.VeryLow => TorrentsFilesReply.Types.FilePriority.Normal, // TODO: missing
                                TorrentContentPriority.Low => TorrentsFilesReply.Types.FilePriority.Normal, // TODO: missing
                                TorrentContentPriority.Normal => TorrentsFilesReply.Types.FilePriority.Normal,
                                TorrentContentPriority.High => TorrentsFilesReply.Types.FilePriority.High,
                                TorrentContentPriority.VeryHigh => TorrentsFilesReply.Types.FilePriority.High, // TODO: missing
                                TorrentContentPriority.Maximal => TorrentsFilesReply.Types.FilePriority.High, // TODO: missing
                                _ => throw new ArgumentOutOfRangeException()
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

        public async Task<TorrentsPeersReply> GetTorrentsPeers(Torrents Req)
        {
            var client = await Client.Init();

            var tasks = Req.Hashes.Select(x => (Hash: x, Peers: client.GetPeerPartialDataAsync(ToNativeHex(x.Span), 0))).ToArray();
            await Task.WhenAll(tasks.Select(x => x.Peers));

            using var scope = ServiceProvider.CreateScope();
            var smas = scope.ServiceProvider.GetRequiredService<ISpeedMovingAverageService>();

            return new TorrentsPeersReply {
                Reply = {
                    tasks.Select(x => {
                        if (x.Peers.Result == null) {
                            return new TorrentsPeersReply.Types.TorrentsPeers {
                                InfoHash = x.Hash,
                                Peers = {}
                            };
                        }

                        Debug.Assert(x.Peers.Result.FullUpdate);

                        Protocols.DataProvider.Torrent? torrent;

                        LatestTorrentsLock.EnterReadLock(); {
                            LatestTorrents.TryGetValue(InfoHash.TruncateV2(x.Hash.Span), out torrent);
                        } LatestTorrentsLock.ExitReadLock();

                        return new TorrentsPeersReply.Types.TorrentsPeers {
                            InfoHash = x.Hash,
                            Peers = {
                                x.Peers.Result.PeersChanged.Select(i => {
                                    ulong peerDLSpeed = 0;

                                    if (i.Value.Progress != 1) {
                                        var sma = smas.Get(InstanceKey, $"PeerDLSpeed_{i.Key}");

                                        if (i.Value.Progress != null && torrent != null) {
                                            sma.ValueIfChanged((long)(torrent.Size * i.Value.Progress.Value));
                                        }

                                        peerDLSpeed = (ulong)sma.CalculateSpeed();
                                    }

                                    TorrentsPeersReply.Types.PeerFlags mapFlag(char Flag)
                                    {
                                        return Flag switch {
                                            'I' => TorrentsPeersReply.Types.PeerFlags.IIncoming,
                                            'E' => TorrentsPeersReply.Types.PeerFlags.EEncrypted,
                                            '?' => TorrentsPeersReply.Types.PeerFlags.SSnubbed, // ???
                                            'u' => TorrentsPeersReply.Types.PeerFlags.SSnubbed, // ???
                                            'S' => TorrentsPeersReply.Types.PeerFlags.SSnubbed, // ???
                                            _ => 0
                                            // TODO: others that do not map...
                                        };
                                    }

                                    var flagArr = i.Value.Flags.Where(x => !Char.IsWhiteSpace(x)).Select(mapFlag);
                                    TorrentsPeersReply.Types.PeerFlags flags = 0;

                                    if (flagArr.Any())
                                        flags = flagArr.Aggregate((a, b) => a | b);

                                    flags |= i.Value.Relevance == 0 ? TorrentsPeersReply.Types.PeerFlags.UUnwanted : 0;

                                    return new TorrentsPeersReply.Types.Peer {
                                        PeerID = Encoding.UTF8.GetBytes(i.Key).ToByteString(),
                                        IPAddress = i.Value.Address.GetAddressBytes().ToByteString(),
                                        Port = (uint)(i.Value.Port ?? 0),
                                        Client = i.Value.Client,
                                        Flags = flags,
                                        Done = (float)(i.Value.Progress * 100 ?? 0),
                                        Downloaded = (ulong)(i.Value.Downloaded ?? 0),
                                        Uploaded = (ulong)(i.Value.Uploaded ?? 0),
                                        DLSpeed = (ulong)(i.Value.DownloadSpeed ?? 0),
                                        UPSpeed = (ulong)(i.Value.UploadSpeed ?? 0),
                                        PeerDLSpeed = peerDLSpeed
                                    };
                                })
                            }
                        };
                    })
                }
            };
        }

        public async Task<Protocols.DataProvider.Torrent> GetTorrent(BytesValue Hash)
        {
            var client = await Client.Init();

            var torrent = await client.GetTorrentListAsync(new TorrentListQuery {
                Hashes = [ ToNativeHex(Hash.Value.Span) ]
            });

            if (!torrent.Any())
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, "Torrent not found"));

            var internalTorrent = new Protocols.DataProvider.Torrent {
                Hash = ByteString.CopyFrom(Hash.Value.Span)
            };

            TorrentMapper.ApplyFromExternal(internalTorrent, torrent.First());
            return internalTorrent;
        }

        public async Task<TorrentsListResponse> GetTorrentList()
        {
            try {
                var client = await Client.Init();

                var all = await client.GetTorrentListAsync();

                var ret = new TorrentsListResponse {
                    List = {
                        all.Select(x => {
                            var internalTorrent = new Protocols.DataProvider.Torrent {
                                Hash = RTSHashFromInfo(x.InfoHashV1, x.InfoHashV2, x.Hash).ToByteString()
                            };
                            TorrentMapper.ApplyFromExternal(internalTorrent, x);
                            return internalTorrent;
                        })
                    }
                };
                return ret;
            } catch (Exception ex) {
                if (ex.IsConnectionFailure()) {
                    throw new DataProviderFailure(ex.Message, ex, Client.Uri);
                }
                throw;
            }
        }

        public async Task GetTorrentListUpdates(GetTorrentListUpdatesRequest Req, IServerStreamWriter<DeltaTorrentsListResponse> Res, CancellationToken CancellationToken)
        {
            var client = await Client.Init();

            // Respond so that client sees we're alive
            await Res.WriteAsync(new(), CancellationToken);

            int rid = 0;

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

            while (!CancellationToken.IsCancellationRequested) {
                PartialData partialData;
                try {
                    partialData = await client.GetPartialDataAsync(rid, CancellationToken);
                } catch (Exception ex) when (ex.IsConnectionFailure()) {
                    throw new DataProviderFailure(ex.Message, ex, Client.Uri);
                }

                rid = partialData.ResponseId;

                delta.FullUpdate.Clear();
                delta.Incomplete.Clear();
                delta.Complete.Clear();
                delta.Removed.Clear();

                async Task<byte[]> resolveRTSHash(string NativeHash, string? V1Hex, string? V2Hex)
                {
                    if (!String.IsNullOrEmpty(V2Hex))
                        return Convert.FromHexString(V2Hex);
                    if (!String.IsNullOrEmpty(V1Hex))
                        return Convert.FromHexString(V1Hex);

                    // Partial row or older qbit lacking the infohash fields: one-shot full fetch to recover them.
                    try {
                        var full = await client.GetTorrentListAsync(new TorrentListQuery { Hashes = [ NativeHash ] });
                        var info = full.Count == 0 ? default : full[0];
                        if (info != null && (!String.IsNullOrEmpty(info.InfoHashV2) || !String.IsNullOrEmpty(info.InfoHashV1)))
                            return RTSHashFromInfo(info.InfoHashV1, info.InfoHashV2, NativeHash);
                    } catch (Exception ex) {
                        Logger.LogTrace(ex, "Failed to resolve rtsHash for {Hash}", NativeHash);
                    }

                    // qbit < 4.4 (no v2 support anyway) or a torrent that could not be resolved.
                    return Convert.FromHexString(NativeHash);
                }

                async Task fullUpdate(string StrHash, TorrentPartialInfo Input)
                {
                    var native = Convert.FromHexString(StrHash);
                    var rtsHash = await resolveRTSHash(StrHash, Input.InfoHashV1, Input.InfoHashV2);
                    var internalTorrent = new Protocols.DataProvider.Torrent {
                        Hash = rtsHash.ToByteString()
                    };
                    TorrentMapper.ApplyFromExternal(internalTorrent, Input);
                    updateHashingSpeed(internalTorrent);

                    LatestTorrentsLock.EnterWriteLock();
                    {
                        LatestTorrents[native] = internalTorrent;
                    }
                    LatestTorrentsLock.ExitWriteLock();

                    delta.FullUpdate.Add(internalTorrent);
                }

                if (partialData.FullUpdate && partialData.TorrentsChanged != null) {
                    foreach (var (strHash, changedTorrent) in partialData.TorrentsChanged) {
                        await fullUpdate(strHash, changedTorrent);
                    }
                } else if (partialData.TorrentsChanged != null) {
                    foreach (var (strHash, changedTorrent) in partialData.TorrentsChanged) {
                        var native = Convert.FromHexString(strHash);

                        if (!LatestTorrents.TryGetValue(native, out var internalTorrent)) {
                            await fullUpdate(strHash, changedTorrent);
                            continue;
                        }

                        var prevState = internalTorrent.State;
                        var prevSequential = internalTorrent.SequentialDownload;
                        var prevWantedSize = internalTorrent.WantedSize;
                        var prevPriority = internalTorrent.Priority;
                        var prevMagnetDummy = internalTorrent.MagnetDummy;
                        var prevRemotePath = internalTorrent.RemotePath;
                        var prevStatusMessage = internalTorrent.StatusMessage;
                        var prevPrimaryTrackerUri = internalTorrent.PrimaryTracker?.Uri;
                        var prevFinishedOn = internalTorrent.FinishedOn;
                        var labelsChanged = changedTorrent.Tags != null && !internalTorrent.Labels.SequenceEqual(changedTorrent.Tags);

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
                            delta.FullUpdate.Add(internalTorrent);
                            continue;
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
                            if (internalTorrent.State.HasFlag(Protocols.DataProvider.TorrentState.Active)) {
                                /*
                                 * qbit pushes so many seemingly fake peer count updates that just jump
                                 * up and down for no reason, so lets only push active complete torrents.
                                 * related? https://github.com/qbittorrent/qBittorrent/issues/22432 ->
                                 * https://github.com/arvidn/libtorrent/issues/7700
                                 */
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
                    }
                }

                if (partialData.TorrentsRemoved != null) {
                    try {

                        LatestTorrentsLock.EnterWriteLock();
                        foreach (var strHash in partialData.TorrentsRemoved) {
                            var native = Convert.FromHexString(strHash);
                            if (LatestTorrents.Remove(native, out var removed)) {
                                delta.Removed.Add(removed.Hash);
                            } else {
                                delta.Removed.Add(native.ToByteString());
                            }
                        }
                    } finally {
                        LatestTorrentsLock.ExitWriteLock();
                    }
                }

                if (delta.Incomplete.Count > 0 || delta.Complete.Count > 0 || delta.Removed.Count > 0 || delta.FullUpdate.Count > 0)
                    await Res.WriteAsync(delta, CancellationToken);
                await Task.Delay(pollInternal, CancellationToken);
            }
        }

        public async Task<BytesValue> MoveDownloadDirectory(MoveDownloadDirectoryArgs Req, CancellationToken CancellationToken)
        {
            var client = await Client.Init();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

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
                            await client.SetLocationAsync(ToNativeHex(req.InfoHash.Span), req.TargetDirectory, cts.Token);
                        } catch (Exception ex) {
                            exception = ex;

                            progress.State = TASK_STATE.FAILED;
                            progress.Text = $"Failed to set location. {ex.Message}";
                            progress.StateData = ex.ToString();
                        }

                        progress.Progress = 100f;
                        progress.State = progress.State == TASK_STATE.FAILED ? TASK_STATE.FAILED : TASK_STATE.DONE;
                        session.Progress.Progress = Interlocked.Increment(ref completedCount) / (float)totalCount * 100;

                        return (req.InfoHash, exception == null ? (IList<Exception>)Array.Empty<Exception>() : [ exception ]);
                    }));
                }

                await Task.WhenAll(tasks);

                session.Progress.Progress = 100f;
                session.Progress.State = TASK_STATE.DONE;
                session.Progress.StateData = "";
                session.Progress.Text = "Done";
            }

            var session = Sessions.RunSession("Moving download directory", cts, fx);

            return session.Id.ToByteArray().ToBytesValue();
        }

        public async Task<TorrentsReply> PerformVoidAction(Torrents In, Func<IQBittorrentClient2, string, Task> Fx)
        {
            var client = await Client.Init();

            var tasks = new List<Task<TorrentsReply.Types.TorrentReply>>();
            foreach (var hash in In.Hashes) {
                tasks.Add(Task.Run(async () => {
                    Exception? exception = null;

                    try {
                        await Fx(client, ToNativeHex(hash.Span));
                    } catch (Exception ex) {
                        exception = ex;
                    }

                    return new TorrentsReply.Types.TorrentReply
                    {
                        InfoHash = hash,
                        Status = {
                            exception == null ? (IList<Protocols.DataProvider.Status>)Array.Empty<Protocols.DataProvider.Status>() : [
                                new Protocols.DataProvider.Status {
                                    Command = "",
                                    FaultString = exception.Message,
                                    FaultCode = "1"
                                }
                            ]
                        }
                    };
                }));
            }

            return new TorrentsReply {
                Torrents = {
                    await Task.WhenAll(tasks)
                }
            };
        }
        
        public Task<TorrentsReply> PauseTorrents(Torrents In) => PerformVoidAction(In, (client, x) => client.PauseAsync(x));

        public Task<TorrentsReply> ReannounceToAllTrackers(Torrents In) => PerformVoidAction(In, (client, x) => client.ReannounceAsync(x));

        public Task<TorrentsReply> RemoveTorrents(Torrents In) => PerformVoidAction(In, (client, x) => client.DeleteAsync(x));

        public Task<TorrentsReply> RemoveTorrentsAndData(Torrents In) => PerformVoidAction(In, (client, x) => client.DeleteAsync(x, deleteDownloadedData: true));

        public async Task<TorrentsReply> SetLabels(SetLabelsArgs Req)
        {
            var client = await Client.Init();

            var tasks = new List<Task<TorrentsReply.Types.TorrentReply>>();
            foreach (var pair in Req.In) {
                var hash = pair.InfoHash;
                var labels = pair.Labels;
                tasks.Add(Task.Run(async () => {
                    Exception? exception = null;

                    try {
                        await client.ClearTorrentTagsAsync(ToNativeHex(hash.Span));
                        await client.AddTorrentTagsAsync(ToNativeHex(hash.Span), labels);
                    } catch (Exception ex) {
                        exception = ex;
                    }

                    return new TorrentsReply.Types.TorrentReply {
                        InfoHash = hash,
                        Status = {
                            exception == null ? (IList<Protocols.DataProvider.Status>)Array.Empty<Protocols.DataProvider.Status>() : [
                                new Protocols.DataProvider.Status {
                                    Command = "",
                                    FaultString = exception.Message,
                                    FaultCode = "1"
                                }
                            ]
                        }
                    };
                }));
            }

            return new TorrentsReply {
                Torrents = {
                    await Task.WhenAll(tasks)
                }
            };
        }

        public Task<TorrentsReply> StartTorrents(Torrents In) => PerformVoidAction(In, (client, x) => client.ResumeAsync(x));

        public Task<TorrentsReply> StopTorrents(Torrents In) => throw new NotSupportedException();

        public async Task<TorrentsPiecesReply> GetTorrentsPieces(Torrents Req)
        {
            var client = await Client.Init();

            var tasks = Req.Hashes.Select(x => (Torrent: x, Pieces: client.GetTorrentPiecesStatesAsync(ToNativeHex(x.Span)))).ToArray();
        
            return new TorrentsPiecesReply {
                Reply = {
                    await Task.WhenAll(tasks.Select(async x => {
                        var pieces = await x.Pieces;

                        if (pieces == null) {
                            return new TorrentsPiecesReply.Types.TorrentsPieces {
                                InfoHash = x.Torrent,
                                Bitfield = ByteString.CopyFrom(0)
                            };
                        }

                        Span<byte> bitfield = new byte[(int)Math.Ceiling(pieces.Count / 8d)];

                        byte current = 0;
                        int currentLeft = 7;
                        for (var i = 0;i < pieces.Count; i++) {
                            if (pieces[i] == TorrentPieceState.Downloaded)
                                current |= (byte)(1 << currentLeft);
                            currentLeft--;

                            if (currentLeft < 0) {
                                bitfield[i >> 3] = current;
                                current = 0;
                                currentLeft = 7;
                            }
                        }
                        if (currentLeft < 7)
                            bitfield[^1] = current;

                        return new TorrentsPiecesReply.Types.TorrentsPieces {
                            InfoHash = x.Torrent,
                            Bitfield = pieces.Count == 0 ? ByteString.CopyFrom(0) : ByteString.CopyFrom(bitfield)
                        };
                    }))
                }
            };
        }

        public async Task<TorrentsTrackersReply> GetTorrentsTrackers(Torrents Req, CancellationToken CancellationToken)
        {
            var client = await Client.Init();

            var groups = Req.Hashes.Chunk(50).Select(x => x.Select(async x => {
                var trackers = await client.GetTorrentTrackersAsync(ToNativeHex(x.Span), CancellationToken);

                if (trackers == null) {
                    return new TorrentsTrackersReply.Types.TorrentsTrackers {
                        InfoHash = x,
                        Trackers = { }
                    };
                }

                return new TorrentsTrackersReply.Types.TorrentsTrackers {
                    InfoHash = x,
                    Trackers = {
                        trackers.Select(x => new Protocols.DataProvider.TorrentTracker {
                            ID = x.Url.OriginalString,
                            Uri = x.Url.ToString(),
                            Status = x.TrackerStatus!.Value switch {
                                QBittorrent.Client.TorrentTrackerStatus.Working => Protocols.DataProvider.TorrentTrackerStatus.Enabled | Protocols.DataProvider.TorrentTrackerStatus.Active,
                                QBittorrent.Client.TorrentTrackerStatus.Updating => Protocols.DataProvider.TorrentTrackerStatus.Enabled,
                                QBittorrent.Client.TorrentTrackerStatus.NotContacted => Protocols.DataProvider.TorrentTrackerStatus.Enabled | Protocols.DataProvider.TorrentTrackerStatus.NotContactedYet,
                                QBittorrent.Client.TorrentTrackerStatus.NotWorking => Protocols.DataProvider.TorrentTrackerStatus.Enabled | Protocols.DataProvider.TorrentTrackerStatus.NotActive,
                                QBittorrent.Client.TorrentTrackerStatus.Disabled => Protocols.DataProvider.TorrentTrackerStatus.Disabled,
                                _ => Protocols.DataProvider.TorrentTrackerStatus.Placeholder
                            },
                            Seeders = (uint)(x.Seeds ?? 0),
                            Peers = (uint)(x.Peers ?? 0),
                            // Where do leeches come in?
                            Downloaded = 0, // TODO: feature flag?
                            LastUpdated = DateTime.UnixEpoch.ToTimestamp(),
                            ScrapeInterval = TimeSpan.Zero.ToDuration(), // TODO: feature flag?
                            StatusMessage = x.Message
                        })
                    }
                };
            }));

            var ret = new List<TorrentsTrackersReply.Types.TorrentsTrackers>();

            foreach (var tasks in groups)
            {
                var result = await Task.WhenAll(tasks);
                ret.AddRange(result);
            }

            return new TorrentsTrackersReply {
                Reply = {
                    ret
                }
            };
        }

        public async Task<Empty> EditTracker(ByteString InfoHash, string Existing, string New, CancellationToken CancellationToken)
        {
            var client = await Client.Init();

            if (!Uri.TryCreate(Existing, UriKind.Absolute, out var existing)) {
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.InvalidArgument, "Existing is not an Uri"));
            }

            if (!Uri.TryCreate(New, UriKind.Absolute, out var @new)) {
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.InvalidArgument, "New is not an Uri"));
            }

            await client.EditTrackerAsync(ToNativeHex(InfoHash.Span), existing, @new, CancellationToken);

            return new();
        }

        public async Task<Protocols.DataProvider.AllTimeDataStats> GetAllTimeDataStats(CancellationToken cancellationToken)
        {
            var client = await Client.Init();

            var stats = await client.GetPartialDataAsync(0, cancellationToken);

            return new Protocols.DataProvider.AllTimeDataStats {
                Download = (ulong)stats.ServerState.AllTimeDownloaded!,
                Upload = (ulong)stats.ServerState.AllTimeUploaded!,
                ShareRatio = (float)stats.ServerState.AllTimeDownloaded.Value / stats.ServerState.AllTimeDownloaded.Value
            };
        }

        public async Task<Empty> AddTracker(ByteString InfoHash, string Tracker, CancellationToken cancellationToken)
        {
            var client = await Client.Init();

            if (!Uri.TryCreate(Tracker, UriKind.Absolute, out var tracker)) {
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.InvalidArgument, "Tracker is not an Uri"));
            }

            await client.AddTrackerAsync(ToNativeHex(InfoHash.Span), tracker, cancellationToken);

            return new();
        }

        public async Task<Empty> RemoveTracker(ByteString InfoHash, string Tracker, CancellationToken cancellationToken)
        {
            var client = await Client.Init();

            if (!Uri.TryCreate(Tracker, UriKind.Absolute, out var tracker)) {
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.InvalidArgument, "Tracker is not an Uri"));
            }

            await client.DeleteTrackerAsync(ToNativeHex(InfoHash.Span), tracker, cancellationToken);

            return new();
        }

        public async Task<Empty> AddPeer(ByteString InfoHash, ByteString IPAddress, uint Port, CancellationToken cancellationToken)
        {
            var client = await Client.Init();

            var ip = new System.Net.IPAddress(IPAddress.Span).ToString();

            await client.AddTorrentPeersAsync([ToNativeHex(InfoHash.Span)], [$"{ip}:{Port}"], cancellationToken);

            return new();
        }

        public async Task<Empty> BanPeer(ByteString InfoHash, ByteString IPAddress, uint Port, CancellationToken cancellationToken)
        {
            var client = await Client.Init();

            var ip = new System.Net.IPAddress(IPAddress.Span).ToString();

            await client.BanPeersAsync([$"{ip}:{Port}"], cancellationToken);

            return new();
        }

        public async Task<Empty> SetSequentialDownload(IEnumerable<ByteString> InfoHashes, bool Enabled, CancellationToken cancellationToken)
        {
            var client = await Client.Init();

            var torrents = await client.GetTorrentListAsync(new TorrentListQuery { Hashes = InfoHashes.Select(x => ToNativeHex(x.Span)) }, cancellationToken);
            var toToggle = torrents.Where(x => x.SequentialDownload != Enabled).Select(x => x.Hash).ToArray();

            await client.ToggleSequentialDownloadAsync(toToggle, cancellationToken);

            return new();
        }

        public async Task<Empty> SetFilePriority(ByteString InfoHash, IEnumerable<uint> Indexes, TorrentsFilesReply.Types.FilePriority Priority, CancellationToken cancellationToken)
        {
            var client = await Client.Init();

            var priority = Priority switch {
                TorrentsFilesReply.Types.FilePriority.DontDownload => TorrentContentPriority.Skip,
                TorrentsFilesReply.Types.FilePriority.Normal => TorrentContentPriority.Normal,
                TorrentsFilesReply.Types.FilePriority.High => TorrentContentPriority.High,
                _ => throw new RpcException(new global::Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown file priority"))
            };

            await client.SetFilePriorityAsync(ToNativeHex(InfoHash.Span), Indexes.Select(x => (int)x), priority, cancellationToken);

            return new();
        }

        public async Task StreamLogs(StreamLogsRequest Req, IServerStreamWriter<LogEntry> Res, CancellationToken CancellationToken)
        {
            var client = await Client.Init();

            int afterId = -1;
            bool first = true;

            while (!CancellationToken.IsCancellationRequested) {
                var entries = (await client.GetLogAsync(TorrentLogSeverity.All, afterId, CancellationToken)).ToList();

                if (entries.Count > 0) {
                    IEnumerable<TorrentLogEntry> toSend = first && entries.Count > Req.History
                        ? entries.Skip(entries.Count - Req.History)
                        : entries;

                    foreach (var entry in toSend) {
                        await Res.WriteAsync(new LogEntry {
                            Timestamp = Timestamp.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(entry.Timestamp).UtcDateTime),
                            Severity = entry.Severity switch {
                                TorrentLogSeverity.Critical => Protocols.DataProvider.LogSeverity.Critical,
                                TorrentLogSeverity.Warning => Protocols.DataProvider.LogSeverity.Warning,
                                TorrentLogSeverity.Info => Protocols.DataProvider.LogSeverity.Info,
                                TorrentLogSeverity.Normal => Protocols.DataProvider.LogSeverity.Notice,
                                _ => Protocols.DataProvider.LogSeverity.Unspecified
                            },
                            Message = entry.Message
                        }, CancellationToken);
                    }

                    afterId = entries[^1].Id;
                }

                first = false;

                if (!Req.Follow)
                    break;

                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
            }
        }
    }
}
