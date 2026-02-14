using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using RTSharp.Daemon.Protocols.DataProvider;

namespace RTSharp.Daemon.GRPCServices.DataProvider
{
    public class TorrentService(ILogger<TorrentService> Logger, IServiceProvider ServiceProvider, RegisteredDataProviders RegisteredDataProviders) : GRPCTorrentService.GRPCTorrentServiceBase
    {
        public override async Task<TorrentsReply> StartTorrents(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().StartTorrents(Req),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().StartTorrents(Req),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().StartTorrents(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
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

        public override async Task<BytesValue> ForceRecheckTorrents(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().ForceRecheckTorrents(Req, Ctx.CancellationToken),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().ForceRecheckTorrents(Req, Ctx.CancellationToken),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().ForceRecheckTorrents(Req, Ctx.CancellationToken),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
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

        public override async Task<TorrentsFilesReply> GetTorrentsFiles(Torrents Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().GetTorrentsFiles(Req),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().GetTorrentsFiles(Req),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().GetTorrentsFiles(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
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
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().MoveDownloadDirectory(Req, Ctx.CancellationToken),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().MoveDownloadDirectory(Req, Ctx.CancellationToken),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().MoveDownloadDirectory(Req, Ctx.CancellationToken),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
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
    }
}
