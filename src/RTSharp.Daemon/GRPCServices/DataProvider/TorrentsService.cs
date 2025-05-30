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
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider")),
            };
        }

        public override async Task GetTorrentListUpdates(GetTorrentListUpdatesRequest Req, IServerStreamWriter<DeltaTorrentsListResponse> Res, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            switch (dp.Type) {
                case DataProviderType.rtorrent:
                    await dp.Resolve<Services.rtorrent.Grpc>().GetTorrentListUpdates(Req, Res, Ctx.CancellationToken);
                    break;
                case DataProviderType.qbittorrent:
                    await dp.Resolve<Services.qbittorrent.Grpc>().GetTorrentListUpdates(Req, Res, Ctx.CancellationToken);
                    break;
                default:
                    throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"));
            }
        }
    }
}
