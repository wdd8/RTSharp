using Grpc.Core;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Services.rtorrent.TorrentPoll;

namespace RTSharp.Daemon.Services.rtorrent
{
    public partial class Grpc
    {
        private TorrentPolling TorrentPolling;
        private SCGICommunication Scgi;
        private TorrentOpService TorrentOpService;
        private ILogger Logger;
        private SessionsService Sessions;
        
        public Grpc([ServiceKey] string InstanceKey, IServiceProvider ServiceProvider, SessionsService Sessions, ILogger<Grpc> Logger)
        {
            TorrentPolling = ServiceProvider.GetRequiredKeyedService<TorrentPolling>(InstanceKey);
            Scgi = ServiceProvider.GetRequiredKeyedService<SCGICommunication>(InstanceKey);
            TorrentOpService = ServiceProvider.GetRequiredKeyedService<TorrentOpService>(InstanceKey);
            this.Sessions = Sessions;
            this.Logger = Logger;
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

                    await Res.WriteAsync(changes);
                }
            }
        }
    }
}
