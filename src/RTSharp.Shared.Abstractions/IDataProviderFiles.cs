using RTSharp.Shared.Utils;

namespace RTSharp.Shared.Abstractions
{
    public interface IDataProviderFiles : IDataProviderBase<DataProviderFilesCapabilities>
    {
        public Task<InfoHashDictionary<byte[]>> GetDotTorrents(IList<byte[]> In);

        public Task<string> GetDefaultSavePath();
    }
}
