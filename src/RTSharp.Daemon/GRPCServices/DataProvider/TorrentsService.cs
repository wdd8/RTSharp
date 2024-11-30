using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Services.rtorrent.TorrentPoll;

namespace RTSharp.Daemon.GRPCServices.DataProvider
{
    public class TorrentsService : GRPCTorrentsService.GRPCTorrentsServiceBase
    {
        private ILogger<TorrentsService> Logger { get; }
        private Services.rtorrent.Grpc RtorrentGrpc { get; }
        private IServiceScopeFactory ScopeFactory { get; }
        private IHostApplicationLifetime Lifetime { get; }


        public TorrentsService(ILogger<TorrentsService> Logger, Services.rtorrent.Grpc RtorrentGrpc, IServiceScopeFactory ScopeFactory, IHostApplicationLifetime Lifetime)
        {
            this.Logger = Logger;
            this.RtorrentGrpc = RtorrentGrpc;
            this.ScopeFactory = ScopeFactory;
            this.Lifetime = Lifetime;
        }

        public override async Task<TorrentsListResponse> GetTorrentList(Empty Req, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            return dp switch {
                DataProviderName.rtorrent => await RtorrentGrpc.GetTorrentList(),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider")),
            };
        }

        public override async Task GetTorrentListUpdates(GetTorrentListUpdatesRequest Req, IServerStreamWriter<DeltaTorrentsListResponse> Res, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            switch (dp) {
                case DataProviderName.rtorrent:
                    await RtorrentGrpc.GetTorrentListUpdates(Req, Res, Ctx.CancellationToken);
                    break;
                default:
                    throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"));
            }
        }
    }
}
