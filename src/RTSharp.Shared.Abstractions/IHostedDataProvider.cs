using Grpc.Core;

namespace RTSharp.Shared.Abstractions
{
    public interface IHostedDataProvider
    {
        DataProviderInstanceConfig DataProviderInstanceConfig { get; }

        public IDataProvider Instance { get; }
        
        Metadata GetBuiltInDataProviderGrpcHeaders();
    }
}
