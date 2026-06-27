using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.DataProvider.Transmission.Plugin;

public class DataProviderStats : IDataProviderStats
{
    private Plugin ThisPlugin { get; }

    public DataProviderStatsCapabilities Capabilities { get; } = new(
        GetStateHistory: false,
        GetAllTimeDataStats: true
    );

    public DataProviderStats(Plugin ThisPlugin)
    {
        this.ThisPlugin = ThisPlugin;
    }

    public Task<AllTimeDataStats> GetAllTimeDataStats(CancellationToken cancellationToken)
    {
        var client = ThisPlugin.Host.AttachedDaemonService!.GetStatsService(ThisPlugin.Host.Instance);

        return client.GetAllTimeDataStats(cancellationToken);
    }
}
