using RTSharp.Shared.Utils;

namespace RTSharp.Shared.Abstractions
{
    public interface IDataProviderTracker : IDataProviderBase<DataProviderTrackerCapabilities>
    {
        public Task<IList<(Uri Uri, IList<Exception> Exceptions)>> Reannounce(byte[] TorrentHash, IList<Uri> TargetUris);
    }
}
