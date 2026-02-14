using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using QBittorrent.Client;

using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

using System.Diagnostics;
using System.Text;

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

        private InfoHashDictionary<Protocols.DataProvider.Torrent> LatestTorrents = new();

        private ReaderWriterLockSlim LatestTorrentsLock = new();

        public string PathCombineFSlash(string a, string b)
        {
            return Path.Combine(a, b).Replace("\\", "/");
        }

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
            var hashes = new HashSet<string>();

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
                    var path = Path.GetTempFileName();
                    await System.IO.File.WriteAllBytesAsync(path, torrent.Data.ToArray());

                    await Client.Client.AddTorrentsAsync(new AddTorrentFilesRequest {
                        DownloadFolder = torrent.Path,
                        Paused = true,
                        TorrentFiles = { path }
                    });

                    System.IO.File.Delete(path);
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

        public async Task<BytesValue> ForceRecheckTorrents(Torrents Req, CancellationToken CancellationToken)
        {
            await Client.Init();

            Exception? exception = null;

            try {
                await Client.Client.RecheckAsync(Req.Hashes.Select(x => Convert.ToHexString(x.Span)));
            } catch (Exception ex) {
                exception = ex;
            }

            Logger.LogInformation("Recheck sent");

            var res = Req.Hashes.Select(x => (Hash: x.ToByteArray(), Exceptions: exception == null ? (IList<Exception>)Array.Empty<Exception>() : ([ exception ]))).ToArray();

            var hashes = Req.Hashes.Select(x => x.ToByteArray()).ToArray();

            var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

            var session = Sessions.CreateSession(cts, async (session) => {
                session.Progress.Text = "Observing state...";
                session.Progress.Progress = 0f;
                session.Progress.State = TASK_STATE.RUNNING;

                var sw = Stopwatch.StartNew();

                var stateObserved = new InfoHashDictionary<QBittorrent.Client.TorrentState>();
                var stateObserved2 = new InfoHashDictionary<QBittorrent.Client.TorrentState>();
                foreach (var torrentResult in res) {
                    stateObserved[torrentResult.Hash] = QBittorrent.Client.TorrentState.Unknown;
                    stateObserved2[torrentResult.Hash] = QBittorrent.Client.TorrentState.Unknown;
                }

                int rid = 1;

                while (true) {
                    if (cts.IsCancellationRequested)
                        break;

                    Logger.LogDebug("Getting partial data inside session");

                    var partialData = await Client.Client.GetPartialDataAsync(rid, CancellationToken);

                    rid = partialData.ResponseId;

                    Logger.LogDebug("Got partial data inside session " + rid);

                    foreach (var hash in hashes) {
                        var hashStr = Convert.ToHexStringLower(hash);

                        var torrentState = partialData.TorrentsChanged?.FirstOrDefault(x => x.Key == hashStr).Value?.State;

                        if (torrentState == null) {
                            Logger.LogDebug($"Torrent {hashStr} no changes");
                            continue;
                        }

                        Logger.LogDebug($"Torrent {hashStr} state: {torrentState.Value}");

                        var currentValue =
                            torrentState.Value == QBittorrent.Client.TorrentState.CheckingDownload ||
                            torrentState.Value == QBittorrent.Client.TorrentState.CheckingUpload ||
                            torrentState.Value == QBittorrent.Client.TorrentState.QueuedForChecking ? QBittorrent.Client.TorrentState.CheckingDownload : torrentState.Value;

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
                    } else if (sw.Elapsed < TimeSpan.FromMinutes(10)) {
                        // Torrents at this point should be hashing or completed hashing

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

        /*public async Task<IEnumerable<Torrent>> GetAllTorrents()
        {
            await Client.Init();

            var list = await Client.Client.GetTorrentListAsync();

            var torrents = list.Select(x => (External: x, Internal: TorrentMapper.MapFromExternal(x)));

            LatestTorrentsLock.EnterWriteLock();
            {
                foreach (var torrent in torrents) {
                    LatestTorrents[torrent.Internal.Hash] = (torrent.Internal, torrent.External);
                }
            }
            LatestTorrentsLock.ExitWriteLock();

            return torrents.Select(x => x.Internal);
        }*/

        public async Task GetDotTorrents(Torrents Req, IServerStreamWriter<DotTorrentsData> Res, CancellationToken CancellationToken)
        {
            await Client.Init();

            var buildUri = Client.Client.GetType().GetMethod("BuildUri", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var client = Client.Client.GetType().GetField("_client", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

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
                    Uri uri = (Uri)buildUri!.Invoke(Client.Client, new object[] { "api/v2/torrents/export", new (string, string)[] { (key: "hash", value: Convert.ToHexString(hash.Span)) } })!;
                    var _client = (HttpClient)client!.GetValue(Client.Client)!;

                    var req = await _client.GetAsync(uri, CancellationToken);
                    var stm = req.Content.ReadAsStream(CancellationToken);

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

        public async Task<TorrentsFilesReply> GetTorrentsFiles(Torrents Req)
        {
            await Client.Init();

            var files = Req.Hashes.Select(x => (Hash: x.ToByteArray(), Files: Client.Client.GetTorrentContentsAsync(Convert.ToHexString(x.Span), null))).ToArray();

            await Task.WhenAll(files.Select(x => x.Files));

            InfoHashDictionary<Protocols.DataProvider.Torrent> torrents;
            LatestTorrentsLock.EnterReadLock();
            {
                torrents = Req.Hashes.Select(x => LatestTorrents[x.ToByteArray()]).ToInfoHashDictionary(x => x.Hash.ToByteArray(), x => x);
            }
            LatestTorrentsLock.ExitReadLock();

            return new TorrentsFilesReply {
                Reply = {
                    files.Select(x => new TorrentsFilesReply.Types.TorrentsFiles {
                        InfoHash = x.Hash.ToByteString(),
                        MultiFile = x.Files.Result[0].Name.Contains('/'),
                        Files = {
                            x.Files.Result.Select(i => {
                                var torrentName = torrents[x.Hash].Name;

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
                                    }
                                };
                            })
                        }
                    })
                }
            };
        }

        public async Task<TorrentsPeersReply> GetTorrentsPeers(Torrents Req)
        {
            await Client.Init();

            var tasks = Req.Hashes.Select(x => (Hash: x, Peers: Client.Client.GetPeerPartialDataAsync(Convert.ToHexString(x.Span), 0))).ToArray();
            await Task.WhenAll(tasks.Select(x => x.Peers));

            using var scope = ServiceProvider.CreateScope();
            var smas = scope.ServiceProvider.GetRequiredService<ISpeedMovingAverageService>();

            return new TorrentsPeersReply {
                Reply = {
                    tasks.Select(x => {
                        Debug.Assert(x.Peers.Result.FullUpdate);

                        Protocols.DataProvider.Torrent? torrent;

                        LatestTorrentsLock.EnterReadLock(); {
                            LatestTorrents.TryGetValue(x.Hash.ToByteArray(), out torrent);
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
            await Client.Init();

            var torrent = await Client.Client.GetTorrentListAsync(new TorrentListQuery {
                Hashes = [ Convert.ToHexString(Hash.Value.Span) ]
            });

            if (!torrent.Any())
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, "Torrent not found"));

            var internalTorrent = new Protocols.DataProvider.Torrent {
                Hash = Hash.ToByteString()
            };
            TorrentMapper.ApplyFromExternal(internalTorrent, torrent.First());
            return internalTorrent;
        }

        public async Task<TorrentsListResponse> GetTorrentList()
        {
            await Client.Init();

            var all = await Client.Client.GetTorrentListAsync();

            var ret = new TorrentsListResponse {
                List = {
                    all.Select(x => {
                        var internalTorrent = new Protocols.DataProvider.Torrent {
                            Hash = Convert.FromHexString(x.Hash).ToByteString()
                        };
                        TorrentMapper.ApplyFromExternal(internalTorrent, x);
                        return internalTorrent;
                    })
                }
            };
            return ret;
        }

        public async Task GetTorrentListUpdates(GetTorrentListUpdatesRequest Req, IServerStreamWriter<DeltaTorrentsListResponse> Res, CancellationToken CancellationToken)
        {
            await Client.Init();

            int rid = 0;

            var pollInternal = Req.Interval.ToTimeSpan();

            var delta = new DeltaTorrentsListResponse();

            while (!CancellationToken.IsCancellationRequested) {
                var partialData = await Client.Client.GetPartialDataAsync(rid, CancellationToken);

                rid = partialData.ResponseId;

                delta.FullUpdate.Clear();
                delta.Incomplete.Clear();
                delta.Complete.Clear();
                delta.Removed.Clear();

                void fullUpdate(string StrHash, TorrentPartialInfo Input)
                {
                    var hash = Convert.FromHexString(StrHash);
                    var internalTorrent = new Protocols.DataProvider.Torrent {
                        Hash = hash.ToByteString()
                    };
                    TorrentMapper.ApplyFromExternal(internalTorrent, Input);

                    LatestTorrentsLock.EnterWriteLock();
                    {
                        LatestTorrents[hash] = internalTorrent;
                    }
                    LatestTorrentsLock.ExitWriteLock();

                    delta.FullUpdate.Add(internalTorrent);
                }

                if (partialData.FullUpdate && partialData.TorrentsChanged != null) {
                    foreach (var (strHash, changedTorrent) in partialData.TorrentsChanged) {
                        fullUpdate(strHash, changedTorrent);
                    }
                } else if (partialData.TorrentsChanged != null) {
                    foreach (var (strHash, changedTorrent) in partialData.TorrentsChanged) {
                        var hash = Convert.FromHexString(strHash);
                        Protocols.DataProvider.Torrent internalTorrent;

                        if (!LatestTorrents.TryGetValue(hash, out var torrent)) {
                            Logger.LogInformation($"New torrent {Convert.ToHexString(hash)} detected: {changedTorrent.Name}");
                            fullUpdate(strHash, changedTorrent);
                            continue;
                        } else {
                            internalTorrent = torrent;

                            var prevState = internalTorrent.State;
                            TorrentMapper.ApplyFromExternal(internalTorrent, changedTorrent);

                            if (internalTorrent.State != prevState) {
                                LatestTorrentsLock.EnterWriteLock();
                                {
                                    LatestTorrents[hash] = internalTorrent;
                                }
                                LatestTorrentsLock.ExitWriteLock();

                                delta.FullUpdate.Add(internalTorrent);
                                continue;
                            } else {
                                LatestTorrentsLock.EnterWriteLock(); { 
                                    LatestTorrents[hash] = internalTorrent;
                                } LatestTorrentsLock.ExitWriteLock();
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
                }

                if (partialData.TorrentsRemoved != null) {
                    try {

                        LatestTorrentsLock.EnterWriteLock();
                        foreach (var strHash in partialData.TorrentsRemoved) {
                            var hash = Convert.FromHexString(strHash);
                            delta.Removed.Add(hash.ToByteString());
                            LatestTorrents.Remove(hash);
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
            await Client.Init();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

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
                            await Client.Client.SetLocationAsync(Convert.ToHexString(req.InfoHash.Span), req.TargetDirectory, cts.Token);
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

                        return (req.InfoHash, exception == null ? (IList<Exception>)Array.Empty<Exception>() : [ exception ]);
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

        public async Task<TorrentsReply> PerformVoidAction(Torrents In, Func<string, Task> Fx)
        {
            await Client.Init();

            var tasks = new List<Task<TorrentsReply.Types.TorrentReply>>();
            foreach (var hash in In.Hashes) {
                tasks.Add(Task.Run(async () => {
                    Exception exception = null;

                    try {
                        await Fx(Convert.ToHexString(hash.Span));
                    } catch (Exception ex) {
                        exception = ex;
                    }

                    return new TorrentsReply.Types.TorrentReply
                    {
                        InfoHash = hash.ToByteArray().ToByteString(),
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
        
        public Task<TorrentsReply> PauseTorrents(Torrents In) => PerformVoidAction(In, x => Client.Client.PauseAsync(x));

        public Task<TorrentsReply> ReannounceToAllTrackers(Torrents In) => PerformVoidAction(In, x => Client.Client.ReannounceAsync(x));

        public Task<TorrentsReply> RemoveTorrents(Torrents In) => PerformVoidAction(In, x => Client.Client.DeleteAsync(x));

        public Task<TorrentsReply> RemoveTorrentsAndData(Torrents In) => PerformVoidAction(In, x => Client.Client.DeleteAsync(x, deleteDownloadedData: true));

        public async Task<TorrentsReply> SetLabels(SetLabelsArgs Req)
        {
            await Client.Init();

            var tasks = new List<Task<TorrentsReply.Types.TorrentReply>>();
            foreach (var pair in Req.In) {
                var hash = pair.InfoHash;
                var labels = pair.Labels;
                tasks.Add(Task.Run(async () => {
                    Exception exception = null;

                    try {
                        await Client.Client.ClearTorrentTagsAsync(Convert.ToHexString(hash.Span));
                        await Client.Client.AddTorrentTagsAsync(Convert.ToHexString(hash.Span), labels);
                    } catch (Exception ex) {
                        exception = ex;
                    }

                    return new TorrentsReply.Types.TorrentReply {
                        InfoHash = hash.ToByteArray().ToByteString(),
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

        public Task<TorrentsReply> StartTorrents(Torrents In) => PerformVoidAction(In, x => Client.Client.ResumeAsync(x));

        public Task<TorrentsReply> StopTorrents(Torrents In) => throw new NotSupportedException();

        public async Task<TorrentsPiecesReply> GetTorrentsPieces(Torrents Req)
        {
            await Client.Init();

            var tasks = Req.Hashes.Select(x => (Torrent: x, Pieces: Client.Client.GetTorrentPiecesStatesAsync(Convert.ToHexString(x.Span)))).ToArray();
        
            return new TorrentsPiecesReply {
                Reply = {
                    await Task.WhenAll(tasks.Select(async x => {
                        var pieces = await x.Pieces;

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
            await Client.Init();

            var groups = Req.Hashes.Chunk(50).Select(x => x.Select(async x => {
                var trackers = await Client.Client.GetTorrentTrackersAsync(Convert.ToHexString(x.Span), CancellationToken);

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
            await Client.Init();

            if (!Uri.TryCreate(Existing, UriKind.Absolute, out var existing)) {
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.InvalidArgument, "Existing is not an Uri"));
            }

            if (!Uri.TryCreate(New, UriKind.Absolute, out var @new)) {
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.InvalidArgument, "New is not an Uri"));
            }

            await Client.Client.EditTrackerAsync(Convert.ToHexString(InfoHash.Span), existing, @new, CancellationToken);

            return new();
        }

        public async Task<Protocols.DataProvider.AllTimeDataStats> GetAllTimeDataStats(CancellationToken cancellationToken)
        {
            await Client.Init();

            var stats = await Client.Client.GetPartialDataAsync(0, cancellationToken);

            return new Protocols.DataProvider.AllTimeDataStats {
                Download = (ulong)stats.ServerState.AllTimeDownloaded!,
                Upload = (ulong)stats.ServerState.AllTimeUploaded!,
                ShareRatio = (float)stats.ServerState.AllTimeDownloaded.Value / stats.ServerState.AllTimeDownloaded.Value
            };
        }
    }
}
