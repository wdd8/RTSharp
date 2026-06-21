using Grpc.Core;

using RTSharp.Daemon.Protocols.DataProvider;

namespace RTSharp.Daemon.GRPCServices.DataProvider
{
    public class LogsService(RegisteredDataProviders RegisteredDataProviders) : GRPCLogsService.GRPCLogsServiceBase
    {
        public override Task StreamLogs(StreamLogsRequest Req, IServerStreamWriter<LogEntry> Res, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            return dp.Type switch {
                DataProviderType.rtorrent => dp.Resolve<Services.rtorrent.Grpc>().StreamLogs(Req, Res, Ctx.CancellationToken),
                DataProviderType.qbittorrent => dp.Resolve<Services.qbittorrent.Grpc>().StreamLogs(Req, Res, Ctx.CancellationToken),
                DataProviderType.transmission => dp.Resolve<Services.transmission.Grpc>().StreamLogs(Req, Res, Ctx.CancellationToken),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }
    }
}
