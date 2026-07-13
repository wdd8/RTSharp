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
        private readonly RTorrentOpService TorrentOpService;
        private readonly V1ToV2InfoHashCache Cache;
        private readonly ILogger Logger;
        private readonly SessionsService Sessions;
        private readonly IServiceProvider ServiceProvider;
        private readonly IOptionsFactory<ConfigModel> Opts;
        private readonly string InstanceKey;

        public Grpc(IOptionsFactory<ConfigModel> Opts, [ServiceKey] string InstanceKey, IServiceProvider ServiceProvider, SessionsService Sessions, V1ToV2InfoHashCache Cache, ILogger<Grpc> Logger)
        {
            TorrentPolling = ServiceProvider.GetRequiredKeyedService<TorrentPolling>(InstanceKey);
            Scgi = ServiceProvider.GetRequiredKeyedService<SCGICommunication>(InstanceKey);
            TorrentOpService = ServiceProvider.GetRequiredKeyedService<RTorrentOpService>(InstanceKey);
            this.Cache = Cache;
            this.Sessions = Sessions;
            this.Logger = Logger;
            this.ServiceProvider = ServiceProvider;
            this.Opts = Opts;
            this.InstanceKey = InstanceKey;
        }

        /// <summary>RTSHash info hash -&gt; rtorrent's native v1 hash. Identity for a v1 hash; NotFound for an unknown v2 hash.</summary>
        private byte[] ToNative(ReadOnlySpan<byte> RTSHash)
        {
            var v1 = Cache.ToV1(RTSHash);
            if (v1 == null)
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, $"Unknown v2 info hash {Convert.ToHexString(RTSHash)}"));
            return v1;
        }

        /// <summary>rtorrent's native v1 hash -&gt; rtsHash info hash (full v2 for hybrids, else identity).</summary>
        private byte[] ToRTSHash(byte[] V1) => Cache.TryGetRTSHash(V1, out var rtsHash) ? rtsHash : V1;
    
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
                Logger.LogDebug("Waiting for updates with interval {interval}...", interval);

                // Respond so that client sees we're alive
                await Res.WriteAsync(new(), CancellationToken);

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
