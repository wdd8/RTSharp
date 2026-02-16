using Grpc.Net.ClientFactory;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Plugin;

using System.Threading;
using System.Threading.Tasks;
using RTSharp.Shared.Abstractions.Daemon;

namespace RTSharp.Core.Services.Daemon
{
    internal class DaemonStatsService : IDaemonStatsService
    {
        GRPCStatsService.GRPCStatsServiceClient StatsClient;
        ILogger<DaemonService> Logger;

        private readonly string ServerId;
        private readonly RTSharpDataProvider DataProvider;

        public DaemonStatsService(RTSharpDataProvider DataProvider)
        {
            this.ServerId = DataProvider.DataProviderInstanceConfig.ServerId;
            this.DataProvider = DataProvider;

            using var scope = Core.ServiceProvider.CreateScope();
            var clientFactory = scope.ServiceProvider.GetRequiredService<GrpcClientFactory>();

            StatsClient = clientFactory.CreateClient<GRPCStatsService.GRPCStatsServiceClient>(nameof(GRPCStatsService.GRPCStatsServiceClient) + "_" + ServerId);

            Logger = scope.ServiceProvider.GetRequiredService<ILogger<DaemonService>>();
        }

        public async Task<Shared.Abstractions.AllTimeDataStats> GetAllTimeDataStats(CancellationToken cancellationToken)
        {
            var res = await StatsClient.GetAllTimeDataStatsAsync(new(), cancellationToken: cancellationToken, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());

            return new Shared.Abstractions.AllTimeDataStats(
                BytesDownloaded: res.Download,
                BytesUploaded: res.Upload,
                ShareRatio: res.ShareRatio
            );
        }
    }
}
