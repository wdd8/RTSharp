using Grpc.Core;
using RTSharp.Daemon.Services.TorrentPoll;
using System.Reactive.Disposables;

namespace RTSharp.Daemon.GRPCServices.DataProvider
{
    public enum DataProviderType
    {
        rtorrent,
        qbittorrent,
        transmission
    }

    public class RegisteredDataProvider(IServiceProvider ServiceProvider, DataProviderType Type, string InstanceKey)
    {
        public string InstanceKey { get; } = InstanceKey;
        public DataProviderType Type { get; } = Type;
        
        public T Resolve<T>() where T : notnull => ServiceProvider.GetRequiredKeyedService<T>(InstanceKey);

        public TorrentPollingService TorrentPolling => Resolve<TorrentPollingService>();

        public override string ToString() => $"{Type}:{InstanceKey}";
    }

    public class RegisteredDataProviders(IServiceProvider ServiceProvider, IConfiguration Configuration)
    {
        public RegisteredDataProvider GetDataProvider(ServerCallContext Ctx)
        {
            var dpNameRaw = Ctx.RequestHeaders.FirstOrDefault(x => x.Key == "data-provider") ?? throw new RpcException(new Status(StatusCode.InvalidArgument, "data-provider header missing"));

            var dp = ServiceProvider.GetKeyedService<RegisteredDataProvider>(dpNameRaw.Value);
            
            if (dp == null)
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"data-provider header '{dpNameRaw.Value}' unknown"));
            
            return dp;
        }

        public RegisteredDataProvider? GetDataProvider(string InstanceKey) => ServiceProvider.GetKeyedService<RegisteredDataProvider>(InstanceKey);

        /// <summary>
        /// Keep every configured provider polling until the returned lease is disposed.
        /// </summary>
        public IDisposable SubscribeToTorrentPolling(TimeSpan Interval)
        {
            if (Interval < TimeSpan.FromMilliseconds(10) || Interval.TotalMilliseconds > uint.MaxValue - 1)
                throw new ArgumentOutOfRangeException(nameof(Interval), "Polling interval must be between 10ms and 49 days.");
            var subscriptions = new CompositeDisposable();
            try {
                foreach (var provider in GetDataProviders())
                    subscriptions.Add(provider.TorrentPolling.Subscribe(Interval));
                return subscriptions;
            } catch {
                subscriptions.Dispose();
                throw;
            }
        }

        public IEnumerable<RegisteredDataProvider> GetDataProviders()
        {
            foreach (var type in Enum.GetValues<DataProviderType>()) {
                foreach (var key in Configuration.GetSection($"DataProviders:{type}").GetChildren().Select(x => x.Key)) {
                    var dp = ServiceProvider.GetKeyedService<RegisteredDataProvider>(key);

                    if (dp != null)
                        yield return dp;
                }
            }
        }
    }
}
