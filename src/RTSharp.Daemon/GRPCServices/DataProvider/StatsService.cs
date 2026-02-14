using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using RTSharp.Daemon.Protocols.DataProvider;

namespace RTSharp.Daemon.GRPCServices.DataProvider
{
    public class StatsService(ILogger<StatsService> Logger, IServiceProvider ServiceProvider, RegisteredDataProviders RegisteredDataProviders) : GRPCStatsService.GRPCStatsServiceBase
    {
        public override async Task<AllTimeDataStats> GetAllTimeDataStats(Empty _, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => throw new NotImplementedException(),
                DataProviderType.qbittorrent => await dp.Resolve<Services.qbittorrent.Grpc>().GetAllTimeDataStats(Ctx.CancellationToken),
                DataProviderType.transmission => await dp.Resolve<Services.transmission.Grpc>().GetAllTimeDataStats(Ctx.CancellationToken),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }
     }
}
