using Grpc.Core;

using Microsoft.Extensions.Options;

using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Services.rtorrent.TorrentPoll;

namespace RTSharp.Daemon.Services.rtorrent
{
    public partial class Grpc
    {
        private readonly TorrentPolling TorrentPolling;
        private readonly SCGICommunication Scgi;
        private readonly TorrentOpService TorrentOpService;
        private readonly ILogger Logger;
        private readonly SessionsService Sessions;
        private readonly IServiceProvider ServiceProvider;
        private readonly IOptionsFactory<ConfigModel> Opts;
        private readonly string InstanceKey;

        public Grpc(IOptionsFactory<ConfigModel> Opts, [ServiceKey] string InstanceKey, IServiceProvider ServiceProvider, SessionsService Sessions, ILogger<Grpc> Logger)
        {
            TorrentPolling = ServiceProvider.GetRequiredKeyedService<TorrentPolling>(InstanceKey);
            Scgi = ServiceProvider.GetRequiredKeyedService<SCGICommunication>(InstanceKey);
            TorrentOpService = ServiceProvider.GetRequiredKeyedService<TorrentOpService>(InstanceKey);
            this.Sessions = Sessions;
            this.Logger = Logger;
            this.ServiceProvider = ServiceProvider;
            this.Opts = Opts;
            this.InstanceKey = InstanceKey;
        }
    
        public async Task<TorrentsListResponse> GetTorrentList()
        {
            var ret = new TorrentsListResponse
            {
                List = { (await TorrentPolling.GetAllTorrents()).List.Values }
            };
            return ret;
        }

        public async Task GetTorrentListUpdates(GetTorrentListUpdatesRequest Req, IServerStreamWriter<DeltaTorrentsListResponse> Res, CancellationToken CancellationToken)
        {
            var interval = Req.Interval.ToTimeSpan();

            if (interval < TimeSpan.FromMilliseconds(10))
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.InvalidArgument, "Interval cannot be lower than 10ms"));

            using (var sub = TorrentPolling.Subscribe(interval)) {
                Logger.LogInformation("Waiting for updates with interval {interval}...", interval);

                while (!CancellationToken.IsCancellationRequested) {
                    var changes = await sub.GetChanges(false, CancellationToken);

                    if (changes == null)
                        return;

                    if (changes.Incomplete.Count > 0 || changes.Complete.Count > 0 || changes.Removed.Count > 0 || changes.FullUpdate.Count > 0)
                        await Res.WriteAsync(changes, CancellationToken);
                }
            }
        }
    }
}
