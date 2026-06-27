using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Abstractions.Client;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.DataProvider.Rtorrent.Plugin;

public class DataProviderStats : IDataProviderStats
{
    private Plugin ThisPlugin { get; }

    public DataProviderStatsCapabilities Capabilities { get; } = new(
        GetStateHistory: false,
        GetAllTimeDataStats: false
    );

    public DataProviderStats(Plugin ThisPlugin)
    {
        this.ThisPlugin = ThisPlugin;
    }

    public Task<AllTimeDataStats> GetAllTimeDataStats(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
