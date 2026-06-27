using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Abstractions.DataProvider;

namespace RTSharp.DataProvider.Qbittorrent.Plugin;

public class DataProviderStats : IDataProviderStats
{
    private Plugin ThisPlugin { get; }
    public IDataProviderHost<QbittorrentDataProviderInstanceConfig> Host => ThisPlugin.Host;

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
        var client = Host.AttachedDaemonService!.GetStatsService(Host.Instance);
        
        return client.GetAllTimeDataStats(cancellationToken);
    }
}
