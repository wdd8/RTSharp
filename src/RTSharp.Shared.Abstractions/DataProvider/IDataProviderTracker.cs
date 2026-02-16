namespace RTSharp.Shared.Abstractions.DataProvider;

public interface IDataProviderTracker : IDataProviderBase<DataProviderTrackerCapabilities>
{
    public Task<IList<(Uri Uri, IList<Exception> Exceptions)>> Reannounce(byte[] TorrentHash, IList<Uri> TargetUris);

    Task ReplaceTracker(Torrent Torrent, string Existing, string New, CancellationToken cancellationToken = default);
}
