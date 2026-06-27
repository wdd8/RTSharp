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

        private readonly string ServerId;
        private readonly RTSharpDataProvider DataProvider;

        public DaemonStatsService(RTSharpDataProvider DataProvider)
        {
            this.ServerId = DataProvider.Config.Plugin.ServerId;
            this.DataProvider = DataProvider;

            StatsClient = Servers.All[ServerId].GetGrpcService<GRPCStatsService.GRPCStatsServiceClient>();
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
