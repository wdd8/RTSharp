using Grpc.Core;

using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Abstractions.DataProvider;

namespace RTSharp.Shared.Abstractions;

public interface IDataProviderHost
{
    DataProviderInstanceConfig DataProviderInstanceConfig { get; }

    public IDataProvider Instance { get; }

    public IPlugin Plugin { get; }

    public IDaemonService AttachedDaemonService { get; }

    Metadata GetBuiltInDataProviderGrpcHeaders();
}
