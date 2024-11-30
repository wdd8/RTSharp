using RTSharp.Shared.Utils;

namespace RTSharp.Shared.Abstractions
{
    public interface IDataProviderFiles : IDataProviderBase<DataProviderFilesCapabilities>
    {
        public Task<string> GetDefaultSavePath();
    }
}
