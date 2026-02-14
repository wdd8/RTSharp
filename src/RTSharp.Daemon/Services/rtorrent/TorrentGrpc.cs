using BencodeNET.Objects;
using BencodeNET.Parsing;
using BencodeNET.Torrents;

using CliWrap;
using CliWrap.Buffered;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Mono.Unix;

using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Services.rtorrent.TorrentPoll;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using File = System.IO.File;
using Status = Grpc.Core.Status;

namespace RTSharp.Daemon.Services.rtorrent
{
    public partial class Grpc
    {
        private TorrentsReply ToTorrentsReply(XMLUtils.TorrentsResult[] In)
        {
            return new TorrentsReply {
                Torrents = {
                    In.Select(x => new TorrentsReply.Types.TorrentReply {
                        InfoHash = x.InfoHash.ToByteString(),
                        Status = {
                            x.Status.Select(GrpcExtensions.ToStatus)
                        }
                    })
                }
            };
        }
    
        public async Task<TorrentsReply> StartTorrents(Torrents Req)
        {
            var hashes = Req.Hashes.Select(x => x.ToByteArray()).ToArray();
            return ToTorrentsReply(await Scgi.XmlActionTorrents(hashes, "d.open", "d.start"));
        }

        public async Task<TorrentsReply> PauseTorrents(Torrents Req)
        {
            var hashes = Req.Hashes.Select(x => x.ToByteArray()).ToArray();
            return ToTorrentsReply(await Scgi.XmlActionTorrents(hashes, "d.stop"));
        }

        public async Task<TorrentsReply> StopTorrents(Torrents Req)
        {
            var hashes = Req.Hashes.Select(x => x.ToByteArray()).ToArray();
            return ToTorrentsReply(await Scgi.XmlActionTorrents(hashes, "d.stop", "d.close"));
        }

        public async Task<BytesValue> ForceRecheckTorrents(Torrents Req, CancellationToken CancellationToken)
        {
            var hashes = Req.Hashes.Select(x => x.ToByteArray()).ToArray();
            var res = await Scgi.XmlActionTorrents(hashes, "d.check_hash");
            
            var successfulRechecks = res.Where(x => x.Status.All(i => i.FaultCode == "0"));

            var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
            
            var session = Sessions.CreateSession(cts, async (session) => {
                session.Progress.Text = "Observing state...";
                session.Progress.Progress = 0f;
                session.Progress.State = TASK_STATE.RUNNING;
                
                var sw = Stopwatch.StartNew();

                var stateObserved = new InfoHashDictionary<bool>();

                using var sub = TorrentPolling.Subscribe(TimeSpan.FromSeconds(1));
                
                while (true) {
                    var changes = await sub.GetChanges(false, cts.Token);
                    
                    if (cts.IsCancellationRequested) {
                        session.Progress.State = TASK_STATE.FAILED;
                        break;
                    }
                    
                    if (sw.Elapsed < TimeSpan.FromSeconds(5)) {
                        foreach (var torrentResult in res) {
                            var torrentState = changes!.FullUpdate.FirstOrDefault(x => x.Hash.SequenceEqual(torrentResult.InfoHash))?.State;
                            torrentState ??= changes.Complete.FirstOrDefault(x => x.Hash.SequenceEqual(torrentResult.InfoHash))?.State;
                            torrentState ??= changes.Incomplete.FirstOrDefault(x => x.Hash.SequenceEqual(torrentResult.InfoHash))?.State;
                            
                            if (torrentState == null)
                                continue;
                            
                            if (torrentState.Value.HasFlag(TorrentState.Hashing)) {
                                stateObserved[torrentResult.InfoHash] = true;
                                break;
                            }
                        }
                    }
                    
                    if (stateObserved.Count != 0) {
                        foreach (var torrentResult in res) {
                            var torrentState = changes!.FullUpdate.FirstOrDefault(x => x.Hash.SequenceEqual(torrentResult.InfoHash))?.State;
                            torrentState ??= changes.Complete.FirstOrDefault(x => x.Hash.SequenceEqual(torrentResult.InfoHash))?.State;
                            torrentState ??= changes.Incomplete.FirstOrDefault(x => x.Hash.SequenceEqual(torrentResult.InfoHash))?.State;
                            
                            if (torrentState == null)
                                continue;
                            
                            if (!torrentState.Value.HasFlag(TorrentState.Hashing)) {
                                stateObserved[torrentResult.InfoHash] = false;
                                break;
                            }
                        }
                    }
                    
                    if (stateObserved.All(x => !x.Value)) {
                        session.Progress.State = TASK_STATE.DONE;
                        break;
                    }
                }
            });
            
            return new BytesValue
            {
                Value = session.Id.ToByteArray().ToByteString()
            };
        }

        public async Task<TorrentsReply> ReannounceToAllTrackers(Torrents Req)
        {
            var hashes = Req.Hashes.Select(x => x.ToByteArray()).ToArray();
            return ToTorrentsReply(await Scgi.XmlActionTorrents(hashes, "d.tracker_announce"));
        }

        record Torrent(string Path, string Filename, MemoryStream Data);

        private async Task<TorrentsReply> AddTorrents(Dictionary<string, Torrent> Torrents)
        {
            var parser = new TorrentParser();
            var xml = new StringBuilder("<?xml version=\"1.0\"?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data>");
            var ret = new TorrentsReply();
            var hashes = new HashSet<string>();

            foreach (var (id, torrent) in Torrents) {
                torrent.Data.Position = 0;

                BencodeNET.Torrents.Torrent btorrent;
                try {
                    btorrent = parser.Parse(torrent.Data);
                } catch {
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

                hashes.Add(btorrent.OriginalInfoHash);

                torrent.Data.Position = 0;

                xml.Append("<value><struct><member><name>methodName</name><value><string>load.raw</string></value></member><member><name>params</name><value><array><data><value><string></string></value><value><base64>");
                xml.Append(Convert.ToBase64String(torrent.Data.ToArray()));
                xml.Append("</base64></value>");

                if (!String.IsNullOrEmpty(torrent.Filename))
                    xml.Append("<value><string>d.custom.set=x-filename," + XMLUtils.EncodeWithSpaces(torrent.Filename) + "</string></value>");

                if (!String.IsNullOrEmpty(torrent.Path))
                    xml.Append("<value><string>d.directory.set=\"" + XMLUtils.Encode(torrent.Path) + "\"</string></value>");

                if (!String.IsNullOrEmpty(btorrent.Comment))
                    xml.Append("<value><string>d.custom2.set=\"" + XMLUtils.Encode(btorrent.Comment) + "\"</string></value>");
                xml.Append("</data></array></value></member></struct></value>");

                torrent.Data.Dispose();

                // TODO: move after Scgi.Get
                ret.Torrents.Add(new TorrentsReply.Types.TorrentReply() {
                    InfoHash = btorrent.OriginalInfoHashBytes.ToByteString(),
                    Status = { GrpcExtensions.SuccessfulStatus("load_raw") }
                });
            }

            xml.Append("</data></array></value></param></params></methodCall>");

            // TODO: more error handling
            Logger.LogInformation($"Add torrent xml: {xml.ToString()}");
            var result = await Scgi.Get(xml.ToString());

            Logger.LogInformation("Add torrent: " + Encoding.UTF8.GetString(result.Span));

            xml.Clear();
            xml.Append("<?xml version=\"1.0\"?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data>");
            foreach (var hash in hashes) {
                xml.Append("<value><struct><member><name>methodName</name><value><string>d.save_full_session</string></value></member><member><name>params</name><value><array><data><value><string>" + hash + "</string></value></data></array></value></member></struct></value>");
            }
            xml.Append("</data></array></value></param></params></methodCall>");
            var result2 = await Scgi.Get(xml.ToString());
            Logger.LogInformation("Sessions: " + Encoding.UTF8.GetString(result2.Span));

            // <?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<methodResponse>\r\n<params>\r\n<param><value><array><data>\r\n</data></array></value></param>\r\n</params>\r\n</methodResponse>\r\n
            // <?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<methodResponse>\r\n<fault>\r\n<value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-503</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Call XML not a proper XML-RPC call.  Incorrect Base64 padding</string></value></member>\r\n</struct></value>\r\n</fault>\r\n</methodResponse>\r\n

            var sw = Stopwatch.StartNew();
            var accountedFor = new InfoHashDictionary<bool>();
            foreach (var hash in hashes) {
                accountedFor[Convert.FromHexString(hash)] = false;
            }

            using (var sub = TorrentPolling.Subscribe(TimeSpan.FromSeconds(1))) {
                while (sw.Elapsed < TimeSpan.FromSeconds(30) && accountedFor.Any(x => !x.Value)) {
                    var changes = await sub.GetChanges(false, default)!;

                    foreach (var (hash, _) in accountedFor) {
                        var torrentHash = changes.FullUpdate.FirstOrDefault(x => x.Hash.SequenceEqual(hash))?.Hash;
                        torrentHash ??= changes.Complete.FirstOrDefault(x => x.Hash.SequenceEqual(hash))?.Hash;
                        torrentHash ??= changes.Incomplete.FirstOrDefault(x => x.Hash.SequenceEqual(hash))?.Hash;

                        if (torrentHash != null) {
                            accountedFor[hash] = true;
                        }
                    }
                }
            }

            XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);
            if (XMLUtils.MaybeSeekFixed(ref result, XMLUtils.FAULT_TOKEN)) {
                XMLUtils.MaybeSeekFixed(ref result, XMLUtils.NEWLINE);
                ret.Torrents.Add(new TorrentsReply.Types.TorrentReply() {
                    InfoHash = Array.Empty<byte>().ToByteString(),
                    Status = { GrpcExtensions.ToStatus(XMLUtils.GetFaultStruct(ref result, "load_raw")) }
                });
                return ret;
            } else {
                return ret;
            }
        }

        public async Task<TorrentsReply> AddTorrents(IAsyncStreamReader<NewTorrentsData> Req)
        {
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

            return await AddTorrents(torrents);
        }

        public async Task<TorrentsFilesReply> GetTorrentsFiles(Torrents Req)
        {
            var basePath = await TorrentOpService.GetTorrentsBasePath(Req.Hashes.Select(x => x.ToByteArray()));
            var ret = await TorrentOpService.GetTorrentsFiles(Req.Hashes.Select(x => x.ToByteArray()));
            
            var pieceSizes = Req.Hashes.ToInfoHashDictionary(x => x.ToByteArray(), x => {
                var entry = PollingSubscription.GetLatestHistoryEntry(x.ToByteArray());
                return entry.Value.Torrent.ChunkSize;
            });

            foreach (var el in ret.Reply) {
                var hash = el.InfoHash.ToByteArray();
                el.MultiFile = basePath[hash].ContainerFolder != null;
                foreach (var file in el.Files) {
                    file.Downloaded = file.DownloadedPieces * pieceSizes[hash];
                }
            }

            return ret;
        }

        public async Task<TorrentsReply> RemoveTorrents(Torrents Req)
        {
            var hashes = Req.Hashes.Select(x => x.ToByteArray()).ToArray();
            return ToTorrentsReply(await Scgi.XmlActionTorrents(hashes, "d.erase"));
        }

        public async Task<TorrentsReply> RemoveTorrentsAndData(Torrents Req)
        {
            var ret = new TorrentsReply();

            foreach (var hash in Req.Hashes) {
                var torrentReply = new TorrentsReply.Types.TorrentReply() {
                    InfoHash = hash
                };
                ret.Torrents.Add(torrentReply);
            }

            var dotTorrentPaths = await TorrentOpService.GetDotTorrentFilePaths(Req.Hashes.Select(x => x.ToByteArray()));
            var dotTorrents = new InfoHashDictionary<byte[]>();

            foreach (var (hash, path) in dotTorrentPaths) {
                var torrentReply = ret.Torrents.Single(x => x.InfoHash.SequenceEqual(hash));

                if (!System.IO.File.Exists(path)) {
                    torrentReply.Status.Add(new Protocols.DataProvider.Status() {
                        Command = "__file_deletion",
                        FaultCode = "2",
                        FaultString = ".torrent file doesn't exist"
                    });
                    continue;
                }

                byte[] torrent;
                try {
                    torrent = await File.ReadAllBytesAsync(path);
                } catch {
                    torrentReply.Status.Add(new Protocols.DataProvider.Status() {
                        Command = "__file_deletion",
                        FaultCode = "3",
                        FaultString = "Cannot read .torrent file"
                    });
                    continue;
                }

                dotTorrents[hash] = torrent;
            }

            string[] actions = [ "d.base_path", "d.delete_tied", "d.erase" ];
            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\"?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data>");
            foreach (var hash in Req.Hashes) {
                var sHash = Convert.ToHexString(hash.Span);

                foreach (var action in actions) {
                    xml.Append("<value><struct><member><name>methodName</name><value><string>" + action + "</string></value></member><member><name>params</name><value><array><data><value><string>" + sHash + "</string></value></data></array></value></member></struct></value>");
                }
            }

            xml.Append("</data></array></value></param></params></methodCall>");

            var result = await Scgi.Get(xml.ToString());

            XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);
            XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_START);

            var basePaths = new InfoHashDictionary<string>();

            foreach (var hash in Req.Hashes) {
                var torrentReply = ret.Torrents.Single(x => x.InfoHash.SequenceEqual(hash));

                foreach (var action in actions) {
                    if (XMLUtils.GetValueType(result) == SCGI_DATA_TYPE.STRUCT) {
                        // <value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-506</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Method 'base_path' not defined</string></value></member>\r\n</struct></value>\r\n<value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-501</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Unsupported target type found.</string></value></member>\r\n</struct></value>\r\n<value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-501</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Unsupported target type found.</string></value></member>\r\n</struct></value>\r\n</data></array></value></param>\r\n</params>\r\n</methodResponse>\r\n
                        // <value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-501</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Unsupported target type found.</string></value></member>\r\n</struct></value>\r\n<value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-501</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Unsupported target type found.</string></value></member>\r\n</struct></value>\r\n<value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-501</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Unsupported target type found.</string></value></member>\r\n</struct></value>\r\n</data></array></value></param>\r\n</params>\r\n</methodResponse>\r\n

                        var status = XMLUtils.GetFaultStruct(ref result, action);
                        torrentReply.Status.Add(GrpcExtensions.ToStatus(status));

                        continue;
                    }

                    // <value><array><data>\r\n<value><string></string></value>\r\n</data></array></value>\r\n<value><array><data>\r\n<value><i4>0</i4></value>\r\n</data></array></value>\r\n<value><array><data>\r\n<value><i4>0</i4></value>\r\n</data></array></value>\r\n</data></array></value></param>\r\n</params>\r\n</methodResponse>\r\n
                    XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);

                    switch (action) {
                        case "d.base_path":
                            var basePath = XMLUtils.Decode(XMLUtils.GetValue<string>(ref result));
                            basePaths[hash.ToByteArray()] = basePath;

                            torrentReply.Status.Add(new Protocols.DataProvider.Status() {
                                Command = action,
                                FaultCode = "0",
                                FaultString = ""
                            });
                            break;
                        default:
                            var resp = XMLUtils.GetValue<int>(ref result);

                            if (resp == 0) {
                                torrentReply.Status.Add(new Protocols.DataProvider.Status() {
                                    Command = action,
                                    FaultCode = "0",
                                    FaultString = ""
                                });
                            } else {
                                torrentReply.Status.Add(new Protocols.DataProvider.Status() {
                                    Command = action,
                                    FaultCode = resp.ToString(),
                                    FaultString = "Response is not 0"
                                });
                            }
                            break;
                    }

                    XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
                }
            }

            var parser = new TorrentParser();

            foreach (var (hash, torrent) in dotTorrents) {
                var basePath = basePaths[hash];
                var torrentReply = ret.Torrents.Single(x => x.InfoHash.SequenceEqual(hash));

                if (String.IsNullOrEmpty(basePath)) {
                    torrentReply.Status.Add(new Protocols.DataProvider.Status() {
                        Command = "__file_deletion",
                        FaultCode = "1",
                        FaultString = "Base path is empty or null"
                    });
                    continue;
                }

                if (basePath[^1] == '/')
                    basePath = basePath[..^1];

                BencodeNET.Torrents.Torrent btorrent;
                try {
                    using (var mem = new MemoryStream(torrent)) {
                        btorrent = parser.Parse(mem);
                    }
                } catch {
                    torrentReply.Status.Add(new Protocols.DataProvider.Status() {
                        Command = "__file_deletion",
                        FaultCode = "4",
                        FaultString = "Cannot parse .torrent file"
                    });
                    continue;
                }

                if (btorrent.File != null) {
                    if (!File.Exists(basePath)) {
                        torrentReply.Status.Add(new Protocols.DataProvider.Status() {
                            Command = "__file_deletion",
                            FaultCode = "5",
                            FaultString = $"File \"{basePath}\" doesn't exist"
                        });
                        continue;
                    }
                    Logger.LogInformation($"Deleting file \"{basePath}\"");
                    System.IO.File.Delete(basePath);
                }

                if (btorrent.Files != null) {
                    var folders = new HashSet<string>() {
                        basePath
                    };
                    foreach (var file in btorrent.Files) {
                        var components = file.PathUtf8 ?? file.Path;

                        if (components == null) {
                            torrentReply.Status.Add(new Protocols.DataProvider.Status() {
                                Command = "__file_deletion",
                                FaultCode = "6",
                                FaultString = "Malformed multi-file torrent"
                            });
                            continue;
                        }

                        var folder = string.Join('/', components.Take(components.Count - 1));
                        if (!String.IsNullOrEmpty(folder))
                            folders.Add(basePath + "/" + folder);

                        var fullPath = basePath + "/" + string.Join('/', components);
                        Logger.LogInformation($"Deleting file \"{fullPath}\"");
                        System.IO.File.Delete(fullPath);
                    }

                    foreach (var folder in folders.OrderByDescending(x => x.Length)) {
                        Logger.LogInformation($"Checking for empty folder: \"{folder}\"");
                        if (!Directory.EnumerateFileSystemEntries(folder).Any()) {
                            Logger.LogInformation($"Deleting empty folder \"{folder}\"");
                            Directory.Delete(folder);
                        }
                    }
                }
            }

            return ret;
        }

        public async Task GetDotTorrents(Torrents Req, IServerStreamWriter<DotTorrentsData> Res, CancellationToken CancellationToken)
        {
            var filePaths = await TorrentOpService.GetDotTorrentFilePaths(Req.Hashes.Select(x => x.ToByteArray()));

            int x = 0;
            foreach (var (hash, path) in filePaths) {
                var strX = x.ToString();

                await Res.WriteAsync(new DotTorrentsData() {
                    TMetadata = new DotTorrentsData.Types.Metadata() {
                        Id = strX,
                        Hash = hash.ToByteString()
                    }
                });

                if (!File.Exists(path)) {
                    await Res.WriteAsync(new DotTorrentsData() {
                        TData = new DotTorrentsData.Types.TorrentData() {
                            Id = strX,
                            Chunk = Array.Empty<byte>().ToByteString()
                        }
                    });
                } else {
                    var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

                    int read = 0;
                    Memory<byte> buffer = new byte[4096];
                    while ((read = await file.ReadAsync(buffer, CancellationToken)) != 0) {
                        await Res.WriteAsync(new DotTorrentsData() {
                            TData = new DotTorrentsData.Types.TorrentData() {
                                Id = strX,
                                Chunk = buffer.Span[..read].ToByteString()
                            }
                        });
                    }
                }

                x++;
            }
        }

        public async Task<TorrentsPeersReply> GetTorrentsPeers(Torrents Req)
        {
            var xml = new StringBuilder("<?xml version=\"1.0\"?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data>");
            foreach (var hash in Req.Hashes) {
                var sHash = Convert.ToHexString(hash.Span);

                xml.Append("<value><struct><member><name>methodName</name><value><string>p.multicall</string></value></member><member><name>params</name><value><array><data><value><string>" + sHash + "</string></value><value><string></string></value><value><string>p.id=</string></value><value><string>p.client_version=</string></value><value><string>p.down_total=</string></value><value><string>p.up_total=</string></value><value><string>p.down_rate=</string></value><value><string>p.up_rate=</string></value><value><string>p.completed_percent=</string></value><value><string>p.peer_rate=</string></value><value><string>p.is_incoming=</string></value><value><string>p.is_encrypted=</string></value><value><string>p.snubbed=</string></value><value><string>p.is_obfuscated=</string></value><value><string>p.is_preferred=</string></value><value><string>p.is_unwanted=</string></value><value><string>p.address=</string></value><value><string>p.port=</string></value></data></array></value></member></struct></value>");
            }
            xml.Append("</data></array></value></param></params></methodCall>");

            var result = await Scgi.Get(xml.ToString());

            XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);
            XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_START);

            Protocols.DataProvider.Status? fault;
            if ((fault = GrpcExtensions.ToStatus(XMLUtils.TryGetFaultStruct(ref result, "p.multicall"))) != null) {
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.Internal, fault.FaultCode + ": " + fault.FaultString));
            }

            XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
            XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);

            var ret = new TorrentsPeersReply();
            foreach (var hash in Req.Hashes) {
                var torrent = new TorrentsPeersReply.Types.TorrentsPeers() {
                    InfoHash = hash
                };

                while (!XMLUtils.CheckFor(result, XMLUtils.DATA_TOKEN_END)) {
                    XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);

                    var peer = new TorrentsPeersReply.Types.Peer {
                        PeerID = Encoding.UTF8.GetBytes(XMLUtils.GetValue<string>(ref result)).ToByteString(),
                        Client = XMLUtils.GetValue<string>(ref result),
                        Downloaded = XMLUtils.GetValue<ulong>(ref result),
                        Uploaded = XMLUtils.GetValue<ulong>(ref result),
                        DLSpeed = XMLUtils.GetValue<ulong>(ref result),
                        UPSpeed = XMLUtils.GetValue<ulong>(ref result),
                        Done = XMLUtils.GetValue<long>(ref result), // ??????
                        PeerDLSpeed = XMLUtils.GetValue<ulong>(ref result)
                    };

                    peer.Flags |= XMLUtils.GetValue<bool>(ref result) ? TorrentsPeersReply.Types.PeerFlags.IIncoming : 0;
                    peer.Flags |= XMLUtils.GetValue<bool>(ref result) ? TorrentsPeersReply.Types.PeerFlags.EEncrypted : 0;
                    peer.Flags |= XMLUtils.GetValue<bool>(ref result) ? TorrentsPeersReply.Types.PeerFlags.SSnubbed : 0;
                    peer.Flags |= XMLUtils.GetValue<bool>(ref result) ? TorrentsPeersReply.Types.PeerFlags.OObfuscated : 0;
                    peer.Flags |= XMLUtils.GetValue<bool>(ref result) ? TorrentsPeersReply.Types.PeerFlags.PPreferred : 0;
                    peer.Flags |= XMLUtils.GetValue<bool>(ref result) ? TorrentsPeersReply.Types.PeerFlags.UUnwanted : 0;

                    var strIp = XMLUtils.GetValue<string>(ref result);

                    if (IPAddress.TryParse(strIp, out var ip))
                        peer.IPAddress = ip.GetAddressBytes().ToByteString();
                    else
                        peer.IPAddress = Array.Empty<byte>().ToByteString();

                    peer.Port = XMLUtils.GetValue<ushort>(ref result);

                    XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);

                    torrent.Peers.Add(peer);
                }

                ret.Reply.Add(torrent);
            }

            return ret;
        }

        [GeneratedRegex(@"^rsync: +\d+ files? to consider$")]
        private static partial Regex RsyncStartRegex();

        [GeneratedRegex(@"^rsync: +(?<bytes>[\d,]+) +(?<percent>\d+%) +(?<speed>\d+(\.\d+)?.B/s) +(?<eta>[\d:]+)( +\(xfr#\d+, to-chk=(?<completeCurrent>\d+)/(?<completeTotal>\d+)\))?")]
        private static partial Regex RsyncSpeedRegex();

        [GeneratedRegex(@"^rsync: (sent [\d,]+ bytes +received [\d,]+ bytes +[\d,\.]+ bytes\/sec)|(total size is [\d,]+ +speedup is [\d\.,]+)$")]
        private static partial Regex RsyncEndRegex();

        public async Task<BytesValue> MoveDownloadDirectory(MoveDownloadDirectoryArgs Req, CancellationToken CancellationToken)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

            async Task fx(ScriptSession session)
            {
                session.Progress.State = TASK_STATE.RUNNING;

                var enumFiles = new ScriptProgressState(session) {
                    Text = "Enumerating files...",
                    Progress = 0f,
                    State = TASK_STATE.WAITING
                };
                var consistencyCheck = new ScriptProgressState(session) {
                    Text = "Consistency check...",
                    Progress = 0f,
                    State = TASK_STATE.WAITING
                };
                var moveState = new ScriptProgressState(session) {
                    Text = "Moving...",
                    Progress = 0f,
                    State = TASK_STATE.WAITING
                };
                var settingPath = new ScriptProgressState(session) {
                    Text = "Setting paths...",
                    Progress = 0f,
                    State = TASK_STATE.WAITING
                };
                var deleteOld = new ScriptProgressState(session) {
                    Text = "Removing old files...",
                    Progress = 0f,
                    State = TASK_STATE.WAITING
                };
                var setState = new ScriptProgressState(session) {
                    Text = "Setting state...",
                    Progress = 0f,
                    State = TASK_STATE.WAITING
                };
                session.Progress.Chain = [enumFiles];

                if (Req.Move) {
                    enumFiles.Chain = [moveState];
                    moveState.Chain = [settingPath];
                } else {
                    enumFiles.Chain = [settingPath];
                }
                if (Req.DeleteSourceFiles) {
                    settingPath.Chain = [deleteOld];
                    deleteOld.Chain = [setState];
                } else {
                    settingPath.Chain = [setState];
                }

                enumFiles.State = TASK_STATE.RUNNING;

                InfoHashDictionary<(string Current, string Target, string? ContainerFolder)> basePaths =
                    (await TorrentOpService.GetTorrentsBasePath(
                        Req.Torrents.Select(x => x.InfoHash.ToByteArray())
                    ))
                    .ToInfoHashDictionary(x => x.Key, x => (
                        x.Value.BasePath,
                        Req.Torrents.First(i =>
                            i.InfoHash.Span.SequenceEqual(x.Key)
                        ).TargetDirectory,
                        x.Value.ContainerFolder
                    ));

                enumFiles.Progress = 50f;

                var files = await TorrentOpService.GetTorrentsFiles(Req.Torrents.Select(x => x.InfoHash.ToByteArray()));

                enumFiles.State = TASK_STATE.DONE;
                consistencyCheck.State = TASK_STATE.RUNNING;

                foreach (var torrent in files.Reply) {
                    setState.Progress = 0f;
                    setState.State = TASK_STATE.WAITING;
                    deleteOld.State = TASK_STATE.WAITING;
                    deleteOld.Progress = 0f;
                    deleteOld.Text = "Removing old files...";
                    settingPath.State = TASK_STATE.WAITING;
                    settingPath.Progress = 0f;
                    settingPath.Text = "Setting paths...";
                    moveState.State = TASK_STATE.WAITING;
                    moveState.Progress = 0f;
                    moveState.Text = "Moving...";

                    if (!basePaths.TryGetValue(torrent.InfoHash.ToByteArray(), out var basePath)) {
                        session.Progress.State = TASK_STATE.FAILED;
                        session.Progress.Text = $"Failed to get base directory for {Convert.ToHexString(torrent.InfoHash.Span)}, terminating operation";
                        session.Progress.StateData = JsonSerializer.Serialize(new { Hash = torrent.InfoHash, File = "", Text = "Failed to get base directory", Success = false });
                        return;
                    }

                    if (!UnixFileSystemInfo.TryGetFileSystemEntry(basePath.Target, out var futureBasePathInfo)) {
                        session.Progress.Text = $"Failed to stat directory of {basePath.Target}";
                        session.Progress.StateData = JsonSerializer.Serialize(new { Hash = torrent.InfoHash, File = basePath.Target, Text = $"Failed to stat directory", Success = false });
                        continue;
                    }

                    if (Req.Move) {
                        if (!UnixFileSystemInfo.TryGetFileSystemEntry(basePath.Current, out var currentBasePathInfo)) {
                            session.Progress.Text = $"Failed to stat directory of {basePath.Current}";
                            session.Progress.StateData = JsonSerializer.Serialize(new { Hash = torrent.InfoHash, File = basePath.Current, Text = $"Failed to stat directory", Success = false });
                            continue;
                        }

                        if (!Directory.Exists(basePath.Target)) {
                            Directory.CreateDirectory(basePath.Target, (UnixFileMode)(int)currentBasePathInfo.FileAccessPermissions);
                        }

                        moveState.State = TASK_STATE.RUNNING;
                        moveState.Text = "Preparing rsync...";

                        var filesFromPath = Path.GetTempFileName();
                        var sourceFiles = torrent.Files.Select(x => (basePath.ContainerFolder != null ? (basePath.ContainerFolder + "/") : "") + x.Path).ToArray();
                        await System.IO.File.WriteAllLinesAsync(filesFromPath, sourceFiles);

                        Command proc;
                        if (currentBasePathInfo.Device == futureBasePathInfo.Device) {
                            proc = Cli.Wrap("rsync")
                                .WithArguments(new[] {
                            "-av",
                            "--progress",
                            "--files-from",
                                filesFromPath,
                            "--link-dest",
                                basePath.Current,

                            basePath.Current,
                            basePath.Target
                                }).WithValidation(CommandResultValidation.None);

                            moveState.Text = "Moving (hardlink)...";
                        } else {
                            proc = Cli.Wrap("rsync")
                                .WithArguments(new[] {
                            "-av",
                            "--progress",
                            "--files-from",
                                filesFromPath,

                            basePath.Current,
                            basePath.Target
                                }).WithValidation(CommandResultValidation.None);

                            moveState.Text = "Moving (with copy)...";
                        }

                        bool startedSending = false, ending = false;
                        var currentFile = "?";

                        proc = proc.WithStandardOutputPipe(PipeTarget.ToDelegate(async x => {
                            if (ending)
                                return;

                            x = x.Trim();
                            if (!startedSending) {
                                if (RsyncStartRegex().IsMatch(x)) {
                                    startedSending = true;
                                }

                                return;
                            }

                            var progressMatch = RsyncSpeedRegex().Match(x);

                            if (progressMatch.Success) {
                                if (!UInt64.TryParse(progressMatch.Groups["bytes"].Value.Replace(",", ""), out var bytesMoved))
                                    return;

                                if (!Converters.TryParseSISpeed(progressMatch.Groups["speed"].Value, true, out var speed))
                                    return;

                                if (!Converters.TryParseTimeSpan(progressMatch.Groups["eta"].Value, out var eta))
                                    return;

                                if (progressMatch.Groups.TryGetValue("completeCurrent", out var sCompleteCurrent) && UInt32.TryParse(sCompleteCurrent.Value, out var completeCurrent)) {
                                    if (progressMatch.Groups.TryGetValue("completeTotal", out var sCompleteTotal) && UInt32.TryParse(sCompleteTotal.Value, out var completeTotal)) {
                                        moveState.Text = $"{torrent.InfoHash} ({completeCurrent}/{completeTotal}): {currentFile} {speed} ETA {eta}";
                                        session.Progress.StateData = JsonSerializer.Serialize(new { Hash = torrent.InfoHash, File = currentFile, Text = $"{bytesMoved}", Success = (bool?)null });
                                    }
                                }
                            } else {
                                if (RsyncEndRegex().IsMatch(x)) {
                                    ending = true;
                                    return;
                                }

                                if (!x.StartsWith("rsync:"))
                                    return;

                                x = x[6..];
                                x = x.TrimStart();

                                if (!String.IsNullOrWhiteSpace(x))
                                    currentFile = x;
                            }
                        }, Encoding.UTF8));
                        proc = proc.WithStandardErrorPipe(PipeTarget.ToDelegate(x => {
                            Logger.LogWarning("rsync: " + x);
                        }));

                        var rsyncResult = await proc.ExecuteAsync();

                        if (rsyncResult.ExitCode != 0) {
                            moveState.Text = "Rsync exited with non-zero exit code " + rsyncResult.ExitCode;
                            continue;
                        }
                    }

                    settingPath.State = TASK_STATE.RUNNING;
                    settingPath.Text = "Setting directories...";

                    await Scgi.XmlActionTorrents(new[] { torrent.InfoHash.ToByteArray() }, "d.stop", "d.close");

                    var xml = new StringBuilder("<?xml version=\"1.0\"?><methodCall><methodName>d.directory.set</methodName><params>");
                    xml.Append("<param><value><string>" + Convert.ToHexString(torrent.InfoHash.Span) + "</string></value></param>");
                    xml.Append("<param><value><string>" + basePath.Target + "</string></value></param>");
                    xml.Append("</params></methodCall>");

                    var result = await Scgi.Get(xml.ToString());

                    // <?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<methodResponse>\r\n<fault>\r\n<value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-503</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Wrong object type.</string></value></member>\r\n</struct></value>\r\n</fault>\r\n</methodResponse>\r\n

                    XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);

                    if (XMLUtils.MaybeSeekFixed(ref result, XMLUtils.FAULT_TOKEN)) {
                        XMLUtils.MaybeSeekFixed(ref result, XMLUtils.NEWLINE);
                        var fault = XMLUtils.GetFaultStruct(ref result, "d.directory.set");

                        settingPath.Text = $"Failed to set directories {fault.FaultCode} ({fault.FaultString})";

                        continue;
                    }

                    if (Req.DeleteSourceFiles) {
                        deleteOld.State = TASK_STATE.RUNNING;
                        deleteOld.Text = "Removing old files...";

                        var sw = Stopwatch.StartNew();
                        var directories = new HashSet<string>();
                        foreach (var file in torrent.Files) {
                            var oldPath = Path.Combine(basePath.Current, basePath.ContainerFolder ?? "", file.Path);

                            try { System.IO.File.Delete(oldPath); } catch { deleteOld.Text = "{torrent.InfoHash}: {oldPath} failed to delete"; }
                            directories.Add(Path.GetDirectoryName(oldPath));

                            if (sw.ElapsedMilliseconds > 100) {
                                deleteOld.Text = "{torrent.InfoHash}: {oldPath}";
                                sw.Restart();
                            }
                        }

                        if (basePath.ContainerFolder != null) {
                            foreach (var dir in directories.Where(dir => !Directory.EnumerateFileSystemEntries(dir).Any())) {
                                try { Directory.Delete(dir, recursive: false); } catch { deleteOld.Text = "{torrent.InfoHash}: {dir} failed to delete"; }

                                if (sw.ElapsedMilliseconds > 100) {
                                    deleteOld.Text = "{torrent.InfoHash}: {dir}";
                                    sw.Restart();
                                }
                            }
                        }
                    }

                    setState.State = TASK_STATE.RUNNING;

                    await Scgi.XmlActionTorrents(new[] { torrent.InfoHash.ToByteArray() }, "d.stop", "d.open", "d.start");

                    setState.Progress = 100f;
                    setState.State = TASK_STATE.DONE;
                }

                session.Progress.State = TASK_STATE.DONE;
                session.Progress.Text = "Done";
            }
            var session = Sessions.CreateSession(cts, fx);

            return session.Id.ToByteArray().ToBytesValue();
        }

        public async Task MoveDownloadDirectoryPreCheck(MoveDownloadDirectoryPreCheckArgs Req, CancellationToken CancellationToken)
        {
            try {
                var proc = await Cli.Wrap("rsync").WithArguments("--version").WithValidation(CommandResultValidation.None).ExecuteBufferedAsync(CancellationToken);
                if (proc.ExitCode != 0) {
                    throw new RpcException(new global::Grpc.Core.Status(StatusCode.FailedPrecondition, "Rsync is not installed. ExitCode " + proc.ExitCode));
                }
            } catch (Exception ex) {
                Logger.LogError(ex, "Rsync installation check error");
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.FailedPrecondition, "Rsync is not installed"));
            }

            InfoHashDictionary<(string Current, string Target, string? ContainerFolder)> basePaths =
                (await TorrentOpService.GetTorrentsBasePath(
                    Req.Torrents.Select(x => x.InfoHash.ToByteArray())
                ))
                .ToInfoHashDictionary(x => x.Key, x => (
                    x.Value.BasePath,
                    Req.Torrents.First(i =>
                        i.InfoHash.SequenceEqual(x.Key)
                    ).TargetDirectory,
                    x.Value.ContainerFolder
                ));

            var files = await TorrentOpService.GetTorrentsFiles(Req.Torrents.Select(x => x.InfoHash.ToByteArray()));

            /* Consistency check */
            {
                var torrentPaths = new List<(string SourceFile, string TargetFile)>();
                foreach (var torrent in files.Reply) {
                    if (!basePaths.TryGetValue(torrent.InfoHash.ToByteArray(), out var basePath))
                        throw new RpcException(new Status(StatusCode.Internal, $"Failed to get base directory for {Convert.ToHexString(torrent.InfoHash.Span)}"));

                    foreach (var file in torrent.Files) {
                        var current = Path.Combine(basePath.Current, basePath.ContainerFolder ?? "", file.Path);
                        var target = Path.Combine(basePath.Target, basePath.ContainerFolder ?? "", file.Path);

                        torrentPaths.Add((current, target));
                    }
                }

                var checkA = torrentPaths.OrderBy(x => x.SourceFile).ToList();
                var checkB = Req.Check.Select(x => (x.SourceFile, x.TargetFile)).OrderBy(x => x.SourceFile).ToArray();

                if (!checkA.SequenceEqual(checkB)) {
                    throw new RpcException(new Status(StatusCode.Unavailable, "Client-side and server-side did not agree on identical list of files"));
                }
            }

            foreach (var torrent in files.Reply) {
                if (!basePaths.TryGetValue(torrent.InfoHash.ToByteArray(), out var basePath))
                    throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Failed to get base directory for {Convert.ToHexString(torrent.InfoHash.Span)}"));

                if (!UnixFileSystemInfo.TryGetFileSystemEntry(basePath.Target, out var futureBasePathInfo)) {
                    throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Failed to stat directory {basePath.Target}"));
                }

                if (Req.Move) {
                    if (!UnixFileSystemInfo.TryGetFileSystemEntry(basePath.Current, out var currentBasePathInfo)) {
                        throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Failed to stat directory {basePath.Current}"));
                    }
                }
            }
        }

        public async Task<TorrentsReply> SetLabels(SetLabelsArgs Req)
        {
            var labels = new InfoHashDictionary<string>();

            foreach (var pair in Req.In) {
                labels.Add(pair.InfoHash.ToByteArray(), String.Join(',', pair.Labels.Select(XMLUtils.Encode).Select(x => x.Replace(",", "\\,"))));
            }

            var input = Req.In.Select(x => (x.InfoHash.ToByteArray(), labels[x.InfoHash.ToByteArray()])).ToArray();
            return ToTorrentsReply(await Scgi.XmlActionTorrentsParam(input, "d.custom1.set", (action, hash, reply) => reply == labels[hash]));
        }

        public async Task<TorrentsPiecesReply> GetTorrentsPieces(Torrents Req)
        {
            var ret = new TorrentsPiecesReply();

            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\"?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data>");
            foreach (var hash in Req.Hashes) {
                var sHash = Convert.ToHexString(hash.Span);

                xml.Append("<value><struct><member><name>methodName</name><value><string>d.bitfield</string></value></member><member><name>params</name><value><array><data><value><string>" + sHash + "</string></value></data></array></value></member></struct></value>");
            }

            xml.Append("</data></array></value></param></params></methodCall>");

            var result = await Scgi.Get(xml.ToString());

            XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);
            XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_START);

            foreach (var hash in Req.Hashes) {
                if (XMLUtils.GetValueType(result) == SCGI_DATA_TYPE.STRUCT) {
                    var status = XMLUtils.GetFaultStruct(ref result, "d.bitfield");

                    // TODO: handle? maybe not?

                    continue;
                }

                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);

                var bitfield = XMLUtils.Decode(XMLUtils.GetValue<string>(ref result));

                ret.Reply.Add(new TorrentsPiecesReply.Types.TorrentsPieces {
                    InfoHash = hash,
                    Bitfield = Convert.FromHexString(bitfield).ToByteString()
                });

                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
            }

            return ret;
        }
        
        public async Task<TorrentsTrackersReply> GetTorrentsTrackers(Torrents Req)
        {
            var ret = new TorrentsTrackersReply();
        
            foreach (var hash in Req.Hashes) {
                var trackers = PollingSubscription.GetTorrentTrackers(hash.ToByteArray());
                
                ret.Reply.Add(new TorrentsTrackersReply.Types.TorrentsTrackers
                {
                    InfoHash = hash,
                    Trackers = { trackers }
                });
            }
            
            return ret;
        }

        public Task<Protocols.DataProvider.Torrent> GetTorrent(BytesValue Hash)
        {
            var torrent = PollingSubscription.GetLatestHistoryEntry(Hash.Value.ToByteArray());

            if (torrent == null)
                throw new RpcException(new Status(StatusCode.NotFound, "Torrent not found"));

            return Task.FromResult(torrent.Value.Torrent);
        }

        public async Task<Empty> EditTracker(ByteString InfoHash, string Existing, string New, CancellationToken cancellationToken)
        {
            var filePaths = await TorrentOpService.GetDotTorrentFilePaths([InfoHash.ToByteArray()]);

            var torrentFilePath = filePaths.First().Value;

            if (!File.Exists(torrentFilePath)) {
                throw new RpcException(new Status(StatusCode.NotFound, "Torrent file not found"));
            }

            var torrentDetails = await Scgi.XmlActionTorrentsMulti([ (InfoHash.ToByteArray(), [ ("d.directory", []), ("d.custom1", []), ("d.custom2", []), ("d.custom", [ "x-filename" ]), ("d.custom", [ "addtime" ]) ]) ]);
            XMLUtils.SeekTo(ref torrentDetails, XMLUtils.METHOD_RESPONSE);
            XMLUtils.SeekFixed(ref torrentDetails, XMLUtils.MULTICALL_START);

            Debugger.Break();

            XMLUtils.SeekFixed(ref torrentDetails, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
            var directory = XMLUtils.GetValue<string>(ref torrentDetails);
            XMLUtils.SeekFixed(ref torrentDetails, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
            XMLUtils.SeekFixed(ref torrentDetails, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
            var label = XMLUtils.GetValue<string>(ref torrentDetails);
            XMLUtils.SeekFixed(ref torrentDetails, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
            XMLUtils.SeekFixed(ref torrentDetails, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
            var comment = XMLUtils.GetValue<string>(ref torrentDetails);
            XMLUtils.SeekFixed(ref torrentDetails, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
            XMLUtils.SeekFixed(ref torrentDetails, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
            var filename = XMLUtils.GetValue<string>(ref torrentDetails);
            XMLUtils.SeekFixed(ref torrentDetails, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
            XMLUtils.SeekFixed(ref torrentDetails, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
            var addtime = XMLUtils.GetValue<string>(ref torrentDetails);

            /*
             *
             */

            using var fileContent = File.OpenRead(torrentFilePath);
            var reader = PipeReader.Create(fileContent);

            var parser = new TorrentParser();
            BencodeNET.Torrents.Torrent btorrent;
            bool trackersReplaced = false;

            try {
                btorrent = await parser.ParseAsync(reader, cancellationToken);
            } catch (Exception ex) {
                Logger.LogError(ex, "Torrent file is not a valid bencode file");
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Torrent file is not a valid bencode file"));
            }

            foreach (var trackers in btorrent.Trackers) {
                var idx = trackers.IndexOf(Existing);

                if (idx != -1) {
                    trackers[idx] = New;
                    trackersReplaced = true;
                }
            }

            if (!trackersReplaced) {
                Logger.LogError("No matching trackers replaced");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "No matching trackers replaced"));
            }

            btorrent.Comment = comment;

            using var mem = new MemoryStream();
            btorrent.EncodeTo(mem);

            var removeReply = await RemoveTorrents(new Torrents {
                Hashes = { InfoHash }
            });

            if (removeReply.Torrents.SingleOrDefault()?.Status.SingleOrDefault()?.FaultCode != "0") {
                Logger.LogError("Failed to remove torrent");
                throw new RpcException(new Status(StatusCode.Unknown, "Failed to remove torrent"));
            }

            var reply = await AddTorrents(new Dictionary<string, Torrent> {
                { "0", new Torrent(directory, filename, mem) }
            });

            if (reply.Torrents.SingleOrDefault()?.Status.SingleOrDefault()?.FaultCode != "0") {
                Logger.LogError("Failed to add torrent");
                throw new RpcException(new Status(StatusCode.Unknown, "Failed to add torrent"));
            }

            // Set label
            await Scgi.XmlActionTorrentsMulti([ (InfoHash.ToByteArray(), [ label ]) ], [ "d.custom1.set" ]);

            // Override addtime later
            Sessions.CreateSession(new(), async session => {
                Logger.LogInformation("Sleeping for 20sec before setting addtime...");
                await Task.Delay(TimeSpan.FromSeconds(20), session.Cts.Token);
                Logger.LogInformation("Setting addtime");
                await Scgi.XmlActionTorrentsMulti([(InfoHash.ToByteArray(), [ "addtime", addtime ])], ["d.custom.set"]);
            });

            return new();
        }

        public async Task<Empty> EditTrackerUntestedOffline(ByteString InfoHash, string Existing, string New, CancellationToken cancellationToken)
        {
            var filePaths = await TorrentOpService.GetDotTorrentFilePaths([ InfoHash.ToByteArray() ]);

            var torrentFilePath = filePaths.First().Value;
            var resumeFilePath = torrentFilePath + ".libtorrent_resume";

            if (!File.Exists(torrentFilePath)) {
                throw new RpcException(new Status(StatusCode.NotFound, "Torrent file not found"));
            }

            if (!File.Exists(resumeFilePath)) {
                throw new RpcException(new Status(StatusCode.NotFound, "Torrent resume file not found"));
            }

            /*
             *
             */

            using var fileContent = File.OpenRead(torrentFilePath);
            var reader = PipeReader.Create(fileContent);

            var parser = new TorrentParser();
            BencodeNET.Torrents.Torrent btorrent;
            bool trackersReplaced = false;

            try {
                btorrent = await parser.ParseAsync(reader, cancellationToken);
            } catch (Exception ex) {
                Logger.LogError(ex, "Torrent file is not a valid bencode file");
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Torrent file is not a valid bencode file"));
            }

            foreach (var trackers in btorrent.Trackers) {
                var idx = trackers.IndexOf(Existing);

                if (idx != -1) {
                    trackers[idx] = New;
                    trackersReplaced = true;
                }
            }

            if (!trackersReplaced) {
                Logger.LogError("No matching trackers replaced");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "No matching trackers replaced"));
            }

            var newFileStm = File.OpenWrite(torrentFilePath + ".trackers_replaced");
            var writer = PipeWriter.Create(newFileStm);
            await btorrent.EncodeToAsync(writer, cancellationToken);

            /*
             * 
             */

            using var resumeFileContent = File.OpenRead(resumeFilePath);
            var resumeReader = PipeReader.Create(resumeFileContent);

            var bencodeParser = new BencodeParser();
            BDictionary bresume;
            bool resumeTrackersReplaced = false;

            try {
                bresume = await bencodeParser.ParseAsync<BDictionary>(resumeReader, cancellationToken);
            } catch (Exception ex) {
                Logger.LogError(ex, "Torrent resume file is not a valid bencode file");
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Torrent resume file is not a valid bencode file"));
            }

            var resumeTrackers = bresume.Get<BDictionary>("trackers");

            if (resumeTrackers == null) {
                Logger.LogError("Torrent resume file does not contain any trackers");
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Torrent resume file does not contain any trackers"));
            }

            foreach (var tracker in resumeTrackers.ToImmutableDictionary()) {
                var url = tracker.Key.ToString();
                if (url == Existing) {
                    resumeTrackers.Add(New, resumeTrackers[Existing]);
                    resumeTrackers.Remove(Existing);
                    resumeTrackersReplaced = true;
                }
            }

            if (!resumeTrackersReplaced) {
                Logger.LogError("No matching trackers replaced (2)");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "No matching trackers replaced (2)"));
            }

            var newResumeFileStm = File.OpenWrite(resumeFilePath + ".trackers_replaced");
            var resumeWriter = PipeWriter.Create(newResumeFileStm);
            await btorrent.EncodeToAsync(writer, cancellationToken);

            /*
             *
             */

            File.Move(torrentFilePath, torrentFilePath + ".old_bak");
            File.Move(torrentFilePath + ".trackers_replaced", torrentFilePath);

            File.Move(resumeFilePath, resumeFilePath + ".old_bak");
            File.Move(resumeFilePath + ".trackers_replaced", resumeFilePath);

            return new();
        }
    }
}
