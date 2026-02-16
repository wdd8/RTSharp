using RTSharp.Shared.Utils;

namespace RTSharp.Shared.Abstractions.DataProvider;

public interface IDataProviderFiles : IDataProviderBase<DataProviderFilesCapabilities>
{
    public Task<string> GetDefaultSavePath();
}
