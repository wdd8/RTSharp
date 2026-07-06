using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

using RTSharp.Daemon.Primitives;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Shared.Utils;

namespace RTSharp.Daemon.GRPCServices.DataProvider
{
    public class TorrentService(RegisteredDataProviders RegisteredDataProviders, Services.TorrentOpService TorrentOps) : GRPCTorrentService.GRPCTorrentServiceBase
    {
        public override Task<TorrentsReply> StartTorrents(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return TorrentOps.StartTorrents(dp, Req);
        }

        public override async Task<TorrentsReply> PauseTorrents(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().PauseTorrents(Req),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().PauseTorrents(Req),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().PauseTorrents(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<TorrentsReply> StopTorrents(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().StopTorrents(Req),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().StopTorrents(Req),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().StopTorrents(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override Task<BytesValue> ForceRecheckTorrents(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return TorrentOps.ForceRecheckTorrents(dp, Req, Ctx.CancellationToken);
        }

        public override Task<TorrentsReply> TryStartSeeding(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return TorrentOps.TryStartSeeding(dp, Req, Ctx.CancellationToken);
        }

        public override async Task<Empty> WaitForTorrentCompletion(BytesValue Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            await TorrentOps.WaitForCompletion(dp, Req.Value.ToByteArray(), Ctx.CancellationToken);
            return new Empty();
        }

        // unused for now.. maybe later?
        public override async Task<Empty> TransmitTorrentData(TransmitTorrentDataInput Req, ServerCallContext Ctx)
        {
            var sourceDp = RegisteredDataProviders.GetDataProvider(Ctx);
            await TorrentOps.TransmitTorrent(sourceDp, Req.Hash.ToByteArray(), new DataProviderEndpoint(Req.TargetUri, Req.TargetProviderKey), Req.SourceUri, Req.TargetPath, Ctx.CancellationToken);
            return new Empty();
        }

        public override Task<BytesValue> ReceiveTorrentData(ReceiveTorrentDataInput Req, ServerCallContext Ctx)
        {
            var targetDp = RegisteredDataProviders.GetDataProvider(Ctx);
            var session = TorrentOps.ReceiveTorrentData(targetDp, Req);
            return Task.FromResult(new BytesValue { Value = session.Id.ToByteString() });
        }

        public override async Task<TorrentsReply> ReannounceToAllTrackers(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().ReannounceToAllTrackers(Req),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().ReannounceToAllTrackers(Req),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().ReannounceToAllTrackers(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<TorrentsReply> AddTorrents(IAsyncStreamReader<NewTorrentsData> Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().AddTorrents(Req),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().AddTorrents(Req),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().AddTorrents(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override Task<TorrentsFilesReply> GetTorrentsFiles(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return TorrentOps.GetTorrentsFiles(dp, Req);
        }

        public override async Task<TorrentsReply> RemoveTorrents(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().RemoveTorrents(Req),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().RemoveTorrents(Req),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().RemoveTorrents(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<TorrentsReply> RemoveTorrentsAndData(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().RemoveTorrentsAndData(Req),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().RemoveTorrentsAndData(Req),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().RemoveTorrentsAndData(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task GetDotTorrents(Torrents Req, IServerStreamWriter<DotTorrentsData> Res, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            await (dp.Type switch {
                DataProviderType.rtorrent => dp.Resolve<Services.rtorrent.Grpc>().GetDotTorrents(Req, Res, Ctx.CancellationToken),
                DataProviderType.qbittorrent => dp.Resolve<Services.qbittorrent.Grpc>().GetDotTorrents(Req, Res, Ctx.CancellationToken),
                DataProviderType.transmission => dp.Resolve<Services.transmission.Grpc>().GetDotTorrents(Req, Res, Ctx.CancellationToken),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            });
        }

        public override async Task<TorrentsPeersReply> GetTorrentsPeers(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().GetTorrentsPeers(Req),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().GetTorrentsPeers(Req),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().GetTorrentsPeers(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<BytesValue> MoveDownloadDirectory(MoveDownloadDirectoryArgs Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            switch (dp.Type) {
                case DataProviderType.rtorrent:
                    if (OperatingSystem.IsWindows()) {
                        throw new RpcException(new Grpc.Core.Status(StatusCode.Unimplemented, "Operating system not supported"));
                    }

                    return await dp.Resolve<Services.rtorrent.Grpc>().MoveDownloadDirectory(Req, Ctx.CancellationToken);
                case DataProviderType.qbittorrent:
                    return await dp.Resolve<Services.qbittorrent.Grpc>().MoveDownloadDirectory(Req, Ctx.CancellationToken);
                case DataProviderType.transmission:
                    return await dp.Resolve<Services.transmission.Grpc>().MoveDownloadDirectory(Req, Ctx.CancellationToken);
                default:
                    throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"));
            };
        }

        public override async Task<Empty> MoveDownloadDirectoryPreCheck(MoveDownloadDirectoryPreCheckArgs Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            switch (dp.Type) {
                case DataProviderType.rtorrent:
                    await dp.Resolve<Services.rtorrent.Grpc>().MoveDownloadDirectoryPreCheck(Req, Ctx.CancellationToken);
                    return new Empty();
                case DataProviderType.qbittorrent:
                    return new Empty();
                default:
                    throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"));
            };
        }

        public override async Task<TorrentsReply> SetLabels(SetLabelsArgs Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().SetLabels(Req),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().SetLabels(Req),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().SetLabels(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<TorrentsPiecesReply> GetTorrentsPieces(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().GetTorrentsPieces(Req),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().GetTorrentsPieces(Req),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().GetTorrentsPieces(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }
        
        public override async Task<TorrentsTrackersReply> GetTorrentsTrackers(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().GetTorrentsTrackers(Req),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().GetTorrentsTrackers(Req, Ctx.CancellationToken),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().GetTorrentsTrackers(Req, Ctx.CancellationToken),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<Empty> QueueTorrentUpdate(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => throw new NotImplementedException(),
                DataProviderType.qbittorrent => throw new NotImplementedException(),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().QueueTorrentUpdate(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<Empty> EditTracker(EditTrackerArgs Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().EditTracker(Req.InfoHash, Req.Existing, Req.New, Ctx.CancellationToken),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().EditTracker(Req.InfoHash, Req.Existing, Req.New, Ctx.CancellationToken),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().EditTracker(Req.InfoHash, Req.Existing, Req.New, Ctx.CancellationToken),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<Empty> AddTracker(SingleTrackerArgs Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => throw new RpcException(new Grpc.Core.Status(StatusCode.Unimplemented, "")),//await dp.Resolve<Services.rtorrent.Grpc>().AddTracker(Req.InfoHash, Req.Tracker, Ctx.CancellationToken),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().AddTracker(Req.InfoHash, Req.Tracker, Ctx.CancellationToken),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().AddTracker(Req.InfoHash, Req.Tracker, Ctx.CancellationToken),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<Empty> RemoveTracker(SingleTrackerArgs Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => throw new RpcException(new Grpc.Core.Status(StatusCode.Unimplemented, "")),//await dp.Resolve<Services.rtorrent.Grpc>().RemoveTracker(Req.InfoHash, Req.Tracker, Ctx.CancellationToken),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().RemoveTracker(Req.InfoHash, Req.Tracker, Ctx.CancellationToken),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().RemoveTracker(Req.InfoHash, Req.Tracker, Ctx.CancellationToken),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<Empty> AddPeer(SinglePeerArgs Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().AddPeer(Req.InfoHash, Req.IPAddress, Req.Port, Ctx.CancellationToken),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().AddPeer(Req.InfoHash, Req.IPAddress, Req.Port, Ctx.CancellationToken),
                DataProviderType.transmission => throw new RpcException(new Grpc.Core.Status(StatusCode.Unimplemented, "")),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<Empty> BanPeer(SinglePeerArgs Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().BanPeer(Req.InfoHash, Req.IPAddress, Req.Port, Ctx.CancellationToken),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().BanPeer(Req.InfoHash, Req.IPAddress, Req.Port, Ctx.CancellationToken),
                DataProviderType.transmission => throw new RpcException(new Grpc.Core.Status(StatusCode.Unimplemented, "")),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<Empty> SetSequentialDownload(SetSequentialDownloadArgs Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => throw new RpcException(new Grpc.Core.Status(StatusCode.Unimplemented, "")),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().SetSequentialDownload(Req.InfoHashes, Req.Enabled, Ctx.CancellationToken),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().SetSequentialDownload(Req.InfoHashes, Req.Enabled, Ctx.CancellationToken),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }
    }
}
