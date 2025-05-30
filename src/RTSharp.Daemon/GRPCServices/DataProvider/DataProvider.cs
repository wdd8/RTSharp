using Grpc.Core;

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
        
        public T Resolve<T>() => ServiceProvider.GetRequiredKeyedService<T>(InstanceKey);
    }

    public class RegisteredDataProviders(IServiceProvider ServiceProvider)
    {
        public RegisteredDataProvider GetDataProvider(ServerCallContext Ctx)
        {
            var dpNameRaw = Ctx.RequestHeaders.FirstOrDefault(x => x.Key == "data-provider") ?? throw new RpcException(new Status(StatusCode.InvalidArgument, "data-provider header missing"));

            var dp = ServiceProvider.GetKeyedService<RegisteredDataProvider>(dpNameRaw.Value);
            
            if (dp == null)
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"data-provider header '{dpNameRaw.Value}' unknown"));
            
            return dp;
        }
    }
}
