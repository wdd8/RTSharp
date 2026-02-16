using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTSharp.Shared.Abstractions.DataProvider;

public interface IDataProviderStats : IDataProviderBase<DataProviderStatsCapabilities>
{
    public Task<AllTimeDataStats> GetAllTimeDataStats(CancellationToken cancellationToken);
}
