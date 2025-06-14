using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using RTSharp.Daemon.Protocols.DataProvider;

namespace RTSharp.Daemon.GRPCServices.DataProvider
{
    public class TorrentsService(ILogger<TorrentsService> Logger, RegisteredDataProviders RegisteredDataProviders, IHostApplicationLifetime Lifetime) : GRPCTorrentsService.GRPCTorrentsServiceBase
    {
        public override async Task<TorrentsListResponse> GetTorrentList(Empty Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => await dp.Resolve<Services.rtorrent.Grpc>().GetTorrentList(),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().GetTorrentList(),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().GetTorrentList(),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider")),
            };
        }

        public override Task GetTorrentListUpdates(GetTorrentListUpdatesRequest Req, IServerStreamWriter<DeltaTorrentsListResponse> Res, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => dp.Resolve<Services.rtorrent.Grpc>().GetTorrentListUpdates(Req, Res, Ctx.CancellationToken),
                DataProviderType.qbittorrent => dp.Resolve<Services.qbittorrent.Grpc>().GetTorrentListUpdates(Req, Res, Ctx.CancellationToken),
                DataProviderType.transmission => dp.Resolve<Services.transmission.Grpc>().GetTorrentListUpdates(Req, Res, Ctx.CancellationToken),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override Task<Torrent> GetTorrent(BytesValue Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => dp.Resolve<Services.rtorrent.Grpc>().GetTorrent(Req),
                DataProviderType.qbittorrent => dp.Resolve<Services.qbittorrent.Grpc>().GetTorrent(Req),
                DataProviderType.transmission => dp.Resolve<Services.transmission.Grpc>().GetTorrent(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }
    }
}
