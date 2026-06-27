using Grpc.Core;

using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Abstractions.DataProvider;

namespace RTSharp.Shared.Abstractions;

/// <summary>
/// Handle to host application through data provider
/// </summary>
public interface IDataProviderHost
{
    public IDataProvider Instance { get; }

    public IPlugin Plugin { get; }

    IPluginConfigContainer<DataProviderInstanceConfig> Config { get; }

    /// <summary>
    /// The daemon service bound to this data provider's <see cref="DataProviderInstanceConfig.ServerId"/>
    /// </summary>
    public IDaemonService AttachedDaemonService { get; }

    Metadata GetBuiltInDataProviderGrpcHeaders();
}