namespace RTSharp.Shared.Abstractions.DataProvider;

public interface IDataProviderFiles : IDataProviderBase<DataProviderFilesCapabilities>
{
    public Task<string> GetDefaultSavePath();

    /// <summary>
    /// Sets the download priority of the given files
    /// </summary>
    /// <param name="Indexes">Indexes into the file list as returned by <see cref="IDataProvider.GetFiles"/></param>
    public Task SetFilePriority(Torrent Torrent, IList<int> Indexes, File.PRIORITY Priority, CancellationToken cancellationToken = default);
}
