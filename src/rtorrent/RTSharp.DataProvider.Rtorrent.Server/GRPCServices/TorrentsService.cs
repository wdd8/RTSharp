using System;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RTSharp.DataProvider.Rtorrent.Protocols;
using RTSharp.DataProvider.Rtorrent.Server.Services;

namespace RTSharp.DataProvider.Rtorrent.Server.GRPCServices
{
    public class TorrentsService : GRPCTorrentsService.GRPCTorrentsServiceBase
    {
        private ILogger<TorrentsService> Logger { get; }
        private TorrentPolling TorrentPolling { get; }
        private IServiceScopeFactory ScopeFactory { get; }
        private IApplicationLifetime Lifetime { get; }


        public TorrentsService(ILogger<TorrentsService> Logger, TorrentPolling TorrentPolling, IServiceScopeFactory ScopeFactory, IApplicationLifetime Lifetime)
        {
            this.Logger = Logger;
            this.TorrentPolling = TorrentPolling;
            this.ScopeFactory = ScopeFactory;
            this.Lifetime = Lifetime;
        }

        public override async Task<TorrentsListResponse> GetTorrentList(Empty Req, ServerCallContext Ctx)
        {
            return await TorrentPolling.GetAllTorrents();
        }

        public override async Task GetTorrentListUpdates(GetTorrentListUpdatesRequest Req, IServerStreamWriter<DeltaTorrentsListResponse> Res, ServerCallContext Ctx)
        {
            var interval = Req.Interval.ToTimeSpan();

            if (interval < TimeSpan.FromMilliseconds(10))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Interval cannot be lower than 10ms"));

            using (var sub = TorrentPolling.Subscribe(interval)) {
                Logger.LogInformation("Waiting for updates with interval {interval}...", interval);

                while (!Ctx.CancellationToken.IsCancellationRequested && !Lifetime.ApplicationStopping.IsCancellationRequested) {
                    var changes = await sub.GetChanges(false, Ctx.CancellationToken);

                    if (changes == null)
                        return;
                
                    await Res.WriteAsync(changes);
                }
            }
        }
    }
}
