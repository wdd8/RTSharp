using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.ClientFactory;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Utils;

namespace RTSharp.Core.Services.Daemon
{
    public class DaemonTorrentsService : IDaemonTorrentsService
    {
        GRPCTorrentService.GRPCTorrentServiceClient TorrentClient;
        GRPCTorrentsService.GRPCTorrentsServiceClient TorrentsClient;
        ILogger<DaemonService> Logger;

        private readonly string ServerId;
        private readonly DataProvider DataProvider;

        public DaemonTorrentsService(DataProvider DataProvider)
        {
            this.ServerId = DataProvider.DataProviderInstanceConfig.ServerId;
            this.DataProvider = DataProvider;

            using var scope = Core.ServiceProvider.CreateScope();
            var clientFactory = scope.ServiceProvider.GetRequiredService<GrpcClientFactory>();

            TorrentClient = clientFactory.CreateClient<GRPCTorrentService.GRPCTorrentServiceClient>(nameof(GRPCTorrentService.GRPCTorrentServiceClient) + "_" + ServerId);
            TorrentsClient = clientFactory.CreateClient<GRPCTorrentsService.GRPCTorrentsServiceClient>(nameof(GRPCTorrentsService.GRPCTorrentsServiceClient) + "_" + ServerId);

            Logger = scope.ServiceProvider.GetRequiredService<ILogger<DaemonService>>();
        }

        public Channel<ListingChanges<Shared.Abstractions.Torrent, byte[]>> GetTorrentChanges(CancellationToken CancellationToken)
        {
            using var scope = Core.ServiceProvider.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<Core.Config>();

            var updates = TorrentsClient.GetTorrentListUpdates(new GetTorrentListUpdatesRequest {
                Interval = Duration.FromTimeSpan(DataProvider.DataProviderInstanceConfig.ListUpdateInterval)
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());

            var channel = System.Threading.Channels.Channel.CreateUnbounded<ListingChanges<Shared.Abstractions.Torrent, byte[]>>(new System.Threading.Channels.UnboundedChannelOptions() {
                SingleReader = true,
                SingleWriter = true
            });

            _ = Task.Run(async () => {
                try {
                    var ownerId = DataProvider.PluginInstance.InstanceId;

                    await foreach (var update in updates.ResponseStream.ReadAllAsync(CancellationToken)) {
                        DataProvider.State.Change(DataProviderState.ACTIVE);
                        var changes = new List<Shared.Abstractions.Torrent>();

                        var incomplete = update.Incomplete.Select(x => {
                            var hash = x.Hash.ToByteArray();
                            if (!Core.TorrentPolling.TorrentPolling.TorrentsLookup.TryGetValue((hash, ownerId), out var cachedTorrent)) {
                                return null; // TODO: what to do?
                            }

                            try {
                                return Mapper.MapFromProto(x, cachedTorrent);
                            } catch {
                                // probe point
                                throw;
                            }
                        });
                        changes.AddRange(incomplete.Where(x => x != null));

                        var complete = update.Complete.Select(x => {
                            var hash = x.Hash.ToByteArray();
                            if (!Core.TorrentPolling.TorrentPolling.TorrentsLookup.TryGetValue((hash, ownerId), out var cachedTorrent)) {
                                return null; // TODO: what to do?
                            }

                            try {
                                return Mapper.MapFromProto(x, cachedTorrent);
                            } catch {
                                // Probe point
                                throw;
                            }
                        });
                        changes.AddRange(complete.Where(x => x != null));

                        var fullUpdate = update.FullUpdate.Select(Mapper.MapFromProto);
                        changes.AddRange(fullUpdate);

                        if (changes.Count != 0 || update.Removed.Count != 0)
                            await channel.Writer.WriteAsync(new ListingChanges<Shared.Abstractions.Torrent, byte[]>(changes, update.Removed.Select(x => x.ToByteArray())), CancellationToken);
                    }

                    channel.Writer.Complete();
                } catch (Exception ex) {
                    // TODO: log or additional handling
                    channel.Writer.Complete(ex);
                } finally {
                    DataProvider.State.Change(DataProviderState.INACTIVE);
                }
            });

            return channel;
        }

        private static TorrentStatuses FromReply(TorrentsReply In)
        {
            var ret = new TorrentStatuses();
            foreach (var torrent in In.Torrents) {
                ret.Add((torrent.InfoHash.ToByteArray(), torrent.Status.Select(x => x.FaultCode is null or "0" ? null : new Exception(x.Command + ": Code " + x.FaultCode + " - " + x.FaultString)).Where(x => x != null).ToList()));
            }
            
            return ret;
        }

        public async Task<TorrentStatuses> RemoveTorrentsAndData(IList<Shared.Abstractions.Torrent> In)
        {
            var result = await TorrentClient.RemoveTorrentsAndDataAsync(new Torrents {
                Hashes = { In.Select(x => x.Hash.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
            
            return FromReply(result);
        }
        
        public async Task<InfoHashDictionary<IList<Tracker>>> GetTorrentsTrackers(IList<Shared.Abstractions.Torrent> In, CancellationToken CancellationToken)
        {
            var result = await TorrentClient.GetTorrentsTrackersAsync(new Torrents {
                Hashes = { In.Select(x => x.Hash.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
            
            return result.Reply.ToInfoHashDictionary(x => x.InfoHash.ToByteArray(), IList<Tracker> (x) => x.Trackers.Select(i => new Tracker
            {
                ID = i.ID,
                Uri = i.Uri,
                Status = FlagsMapper.Map(i.Status, a => a switch {
                    TorrentTrackerStatus.Active => Tracker.TRACKER_STATUS.ACTIVE,
                    TorrentTrackerStatus.NotActive => Tracker.TRACKER_STATUS.NOT_ACTIVE,
                    TorrentTrackerStatus.NotContactedYet => Tracker.TRACKER_STATUS.NOT_CONTACTED_YET,
                    TorrentTrackerStatus.Disabled => Tracker.TRACKER_STATUS.DISABLED,
                    TorrentTrackerStatus.Enabled => Tracker.TRACKER_STATUS.ENABLED,
                    _ => throw new ArgumentOutOfRangeException()
                }),
                Seeders = i.Seeders,
                Peers = i.Peers,
                Downloaded = i.Downloaded,
                LastUpdated = i.LastUpdated.ToDateTime(),
                Interval = i.ScrapeInterval.ToTimeSpan(),
                StatusMsg = i.StatusMessage
            }).ToList());
        }
        
        public async Task<InfoHashDictionary<byte[]>> GetDotTorrents(IList<Shared.Abstractions.Torrent> In)
        {
            var res = TorrentClient.GetDotTorrents(new Torrents {
                Hashes = { In.Select(x => x.Hash.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
            
            var meta = new Dictionary<string, byte[]>();
            var dotTorrents = new Dictionary<string, System.IO.MemoryStream>();

            await foreach (var data in res.ResponseStream.ReadAllAsync()) {
                switch (data.DataCase) {
                    case DotTorrentsData.DataOneofCase.TMetadata:
                        if (meta.ContainsKey(data.TMetadata.Id)) {
                            throw new InvalidOperationException("Received duplicate metadata on hash");
                        }

                        dotTorrents[data.TMetadata.Id] = new System.IO.MemoryStream();
                        meta[data.TMetadata.Id] = data.TMetadata.Hash.ToByteArray();
                        break;
                    case DotTorrentsData.DataOneofCase.TData:
                        dotTorrents[data.TData.Id].Write(data.TData.Chunk.Span);
                        break;
                }
            }

            var ret = new InfoHashDictionary<byte[]>();
            foreach (var (k, v) in dotTorrents) {
                ret[meta[k]] = v.ToArray();
                v.Dispose();
            }
            
            return ret;
        }
        
        public async Task<InfoHashDictionary<IList<Peer>>> GetTorrentsPeers(IList<Shared.Abstractions.Torrent> In, CancellationToken cancellationToken)
        {
            var result = await TorrentClient.GetTorrentsPeersAsync(new Torrents {
                Hashes = { In.Select(x => x.Hash.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders(), cancellationToken: cancellationToken);

            return result.Reply.ToInfoHashDictionary(x => x.InfoHash.ToByteArray(), IList<Peer> (x) => x.Peers.Select(i => new Peer
            {
                PeerId = i.PeerID,
                IPPort = new IPEndPoint(new IPAddress(i.IPAddress.ToByteArray()), (ushort)i.Port),
                Client = i.Client,
                Flags = FlagsMapper.Map(i.Flags, a => a switch {
                    TorrentsPeersReply.Types.PeerFlags.IIncoming => Shared.Abstractions.Peer.PEER_FLAGS.I_INCOMING,
                    TorrentsPeersReply.Types.PeerFlags.EEncrypted => Shared.Abstractions.Peer.PEER_FLAGS.E_ENCRYPTED,
                    TorrentsPeersReply.Types.PeerFlags.SSnubbed => Shared.Abstractions.Peer.PEER_FLAGS.S_SNUBBED,
                    TorrentsPeersReply.Types.PeerFlags.OObfuscated => Shared.Abstractions.Peer.PEER_FLAGS.O_OBFUSCATED,
                    TorrentsPeersReply.Types.PeerFlags.PPreferred => Shared.Abstractions.Peer.PEER_FLAGS.P_PREFERRED,
                    TorrentsPeersReply.Types.PeerFlags.UUnwanted => Shared.Abstractions.Peer.PEER_FLAGS.U_UNWANTED,
                    _ => throw new ArgumentOutOfRangeException(nameof(i.Flags), i.Flags, null)
                }),
                Done = i.Done,
                Downloaded = i.Downloaded,
                Uploaded = i.Uploaded,
                DLSpeed = i.DLSpeed, 
                UPSpeed = i.UPSpeed,
                PeerDLSpeed = i.PeerDLSpeed
            }).ToList());
        }
        
        public async Task<InfoHashDictionary<(bool MultiFile, IList<File> Files)>> GetTorrentsFiles(IList<Shared.Abstractions.Torrent> In, CancellationToken cancellationToken)
        {
            var resp = await TorrentClient.GetTorrentsFilesAsync(new Torrents {
                Hashes = { In.Select(x => x.Hash.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders(), cancellationToken: cancellationToken);

            var ret = new InfoHashDictionary<(bool MultiFile, IList<File> Files)>();
            
            foreach (var torrent in resp.Reply) {
                var files = new List<File>();
                files.AddRange(torrent.Files.Select(x => new File
                {
                    Path = x.Path,
                    Size = x.Size,
                    DownloadedPieces = x.DownloadedPieces,
                    Downloaded = x.Downloaded,
                    DownloadStrategy = x.DownloadStrategy switch {
                        TorrentsFilesReply.Types.FileDownloadStrategy.Normal => File.DOWNLOAD_STRATEGY.NORMAL,
                        TorrentsFilesReply.Types.FileDownloadStrategy.PrioritizeFirst => File.DOWNLOAD_STRATEGY.PRIORITIZE_FIRST,
                        TorrentsFilesReply.Types.FileDownloadStrategy.PrioritizeLast => File.DOWNLOAD_STRATEGY.PRIORITIZE_LAST,
                        _ => File.DOWNLOAD_STRATEGY.NA
                    },
                    Priority = x.Priority switch {
                        TorrentsFilesReply.Types.FilePriority.DontDownload => File.PRIORITY.DONT_DOWNLOAD,
                        TorrentsFilesReply.Types.FilePriority.Normal => File.PRIORITY.NORMAL,
                        TorrentsFilesReply.Types.FilePriority.High => File.PRIORITY.HIGH,
                        _ => File.PRIORITY.NA
                    }
                }));

                ret[torrent.InfoHash.ToByteArray()] = (torrent.MultiFile, files);
            }

            return ret;
        }
        
        public async Task<IEnumerable<Shared.Abstractions.Torrent>> GetAllTorrents(CancellationToken cancellationToken)
        {
            var resp = await TorrentsClient.GetTorrentListAsync(new Empty(
            
            ), headers: DataProvider.GetBuiltInDataProviderGrpcHeaders(), cancellationToken: cancellationToken);
            
            return resp.List.Select(Mapper.MapFromProto);
        }
        
        public async Task<TorrentStatuses> StartTorrents(IList<byte[]> Hashes)
        {
            var resp = await TorrentClient.StartTorrentsAsync(new Torrents {
                Hashes = { Hashes.Select(x => x.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
            
            return FromReply(resp);
        }
        
        public async Task<TorrentStatuses> PauseTorrents(IList<byte[]> Hashes)
        {
            var resp = await TorrentClient.PauseTorrentsAsync(new Torrents {
                Hashes = { Hashes.Select(x => x.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
            
            return FromReply(resp);
        }
        
        public async Task<TorrentStatuses> StopTorrents(IList<byte[]> Hashes)
        {
            var resp = await TorrentClient.StopTorrentsAsync(new Torrents {
                Hashes = { Hashes.Select(x => x.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
            
            return FromReply(resp);
        }
        
        public async Task<Guid> ForceRecheckTorrents(IList<byte[]> Hashes)
        {
            var resp = await TorrentClient.ForceRecheckTorrentsAsync(new Torrents {
                Hashes = { Hashes.Select(x => x.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
            
            return new Guid(resp.Value.Span);
        }
        
        public async Task<TorrentStatuses> AddTorrents(IList<(byte[] Data, string? Filename, AddTorrentsOptions Options)> In)
        {
            var call = TorrentClient.AddTorrents(headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
            
            for (var x = 0;x < In.Count;x++) {
                await call.RequestStream.WriteAsync(new NewTorrentsData {
                    TMetadata = new NewTorrentsData.Types.Metadata {
                        Id = x.ToString(),
                        Path = In[x].Options.RemoteTargetPath,
                        Filename = In[x].Filename ?? ""
                    }
                });
            }
            for (var x = 0;x < In.Count;x++) {
                foreach (var chunk in In[x].Data.Chunk(4096)) {
                    await call.RequestStream.WriteAsync(new NewTorrentsData {
                        TData = new NewTorrentsData.Types.TorrentData {
                            Id = x.ToString(),
                            Chunk = chunk.ToByteString()
                        }
                    });
                }
            }

            await call.RequestStream.CompleteAsync();

            var resp = await call.ResponseAsync;
            return FromReply(resp);
        }
        
        public async Task<TorrentStatuses> ReannounceToAllTrackers(IList<byte[]> Hashes)
        {
            var resp = await TorrentClient.ReannounceToAllTrackersAsync(new Torrents {
                Hashes = { Hashes.Select(x => x.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
            
            return FromReply(resp);
        }
        
        public async Task<TorrentStatuses> RemoveTorrents(IList<byte[]> Hashes)
        {
            var resp = await TorrentClient.RemoveTorrentsAsync(new Torrents {
                Hashes = { Hashes.Select(x => x.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
            
            return FromReply(resp);
        }
        
        public async Task<TorrentStatuses> SetLabels(IList<(byte[] Hash, string[] Labels)> In)
        {
            var reply = await TorrentClient.SetLabelsAsync(new SetLabelsArgs {
                In = {
                    In.Select(x => new SetLabelsArgs.Types.TorrentLabelsPair {
                        InfoHash = x.Hash.ToByteString(),
                        Labels = { x.Labels }
                    })
                }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
            
            return FromReply(reply);
        }
        
        public async Task<InfoHashDictionary<IList<PieceState>>> GetPieces(IList<Shared.Abstractions.Torrent> In, CancellationToken cancellationToken)
        {
            var res = new InfoHashDictionary<IList<PieceState>>();

            var reply = await TorrentClient.GetTorrentsPiecesAsync(new Torrents {
                Hashes = { In.Select(x => x.Hash.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders(), cancellationToken: cancellationToken);

            foreach (var torrent in reply.Reply) {
                var totalBits = torrent.Bitfield.Length * 8;
                var pieces = new PieceState[totalBits];
                for (var x = 0;x < totalBits;x += 8) {
                    var bitset = torrent.Bitfield[x >> 3]; // / 8
                    for (var i = 0;i < 8;i++) {
                        if ((bitset & 0x80) == 0)
                            pieces[x+i] = PieceState.NotDownloaded;
                        else
                            pieces[x+i] = PieceState.Downloaded;
                        bitset <<= 1;
                    }
                }

                res[torrent.InfoHash.ToByteArray()] = pieces;
            }
            return res;
        }

        public async Task<string?> MoveDownloadDirectoryPreCheck(InfoHashDictionary<string> Torrents, IList<(string SourceFile, string TargetFile)> Check, bool Move)
        {
            try {
                await TorrentClient.MoveDownloadDirectoryPreCheckAsync(new MoveDownloadDirectoryPreCheckArgs {
                    Check = {
                        Check.Select(x => new MoveDownloadDirectoryPreCheckArgs.Types.SourceTargetCheck {
                            SourceFile = x.SourceFile,
                            TargetFile = x.TargetFile
                        })
                    },
                    Torrents = {
                        Torrents.Select(x => new MoveDownloadDirectoryPreCheckArgs.Types.TorrentMoveDownloadDirectory {
                            InfoHash = x.Key.ToByteString(),
                            TargetDirectory = x.Value
                        })
                    },
                    Move = Move
                }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
            } catch (RpcException ex) {
                return ex.Message;
            }

            return null;
        }

        public async Task<Guid> MoveDownloadDirectory(InfoHashDictionary<string> Torrents, bool Move, bool DeleteSourceFiles)
        {
            var session = await TorrentClient.MoveDownloadDirectoryAsync(new MoveDownloadDirectoryArgs {
                Torrents = {
                    Torrents.Select(x => new MoveDownloadDirectoryArgs.Types.TorrentMoveDownloadDirectory {
                        InfoHash = x.Key.ToByteString(),
                        TargetDirectory = x.Value
                    })
                },
                Move = Move,
                DeleteSourceFiles = DeleteSourceFiles
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());

            return new Guid(session.Value.Span);
        }

        public async Task<Shared.Abstractions.Torrent> GetTorrent(byte[] Hash)
        {
            var resp = await TorrentsClient.GetTorrentAsync(Hash.ToBytesValue(), headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());

            return Mapper.MapFromProto(resp);
        }

        public async Task QueueTorrentUpdate(IList<byte[]> Hashes)
        {
            await TorrentClient.QueueTorrentUpdateAsync(new Torrents {
                Hashes = { Hashes.Select(x => x.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
        }
    }
}
