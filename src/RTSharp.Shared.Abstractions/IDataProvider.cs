using RTSharp.Shared.Utils;

namespace RTSharp.Shared.Abstractions
{
    public interface IDataProvider : IDataProviderBase<DataProviderCapabilities>
    {
        public Task<IEnumerable<Torrent>> GetAllTorrents();

        public Task<Torrent> GetTorrent(byte[] Hash);

        public Task<System.Threading.Channels.ChannelReader<ListingChanges<Torrent, byte[]>>> GetTorrentChanges();

        public Task<InfoHashDictionary<IList<Peer>>> GetPeers(IList<Torrent> In, CancellationToken cancellationToken = default);

        public Task<InfoHashDictionary<(bool MultiFile, IList<File> Files)>> GetFiles(IList<Torrent> In, CancellationToken cancellationToken = default);

        public Task<InfoHashDictionary<IList<Tracker>>> GetTrackers(IList<Torrent> In, CancellationToken cancellationToken = default);

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> StartTorrents(IList<byte[]> In);

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> PauseTorrents(IList<byte[]> In);

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> StopTorrents(IList<byte[]> In);

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> AddTorrents(IList<(byte[] Data, string? Filename, AddTorrentsOptions Options)> In);

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> ForceRecheck(IList<byte[]> In);

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> ReannounceToAllTrackers(IList<byte[]> In);

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> RemoveTorrents(IList<byte[]> In);

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> RemoveTorrentsAndData(IList<byte[]> In);

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> MoveDownloadDirectory(IList<(byte[] InfoHash, string TargetDirectory)> In, IList<(string SourceFile, string TargetFile)> Check, IProgress<(byte[] InfoHash, string File, ulong Moved, string? AdditionalProgress)> Progress);

        public Task<InfoHashDictionary<byte[]>> GetDotTorrents(IList<byte[]> In);

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> SetLabels(IList<(byte[] Hash, string[] Labels)> In);

        public IPlugin Plugin { get; }

        public Notifyable<long> LatencyMs { get; }

        public Notifyable<long> TotalDLSpeed { get; }

        public Notifyable<long> TotalUPSpeed { get; }

        public Notifyable<long> ActiveTorrentCount { get; }

        public Notifyable<DataProviderState> State { get; }

        public IDataProviderFiles Files { get; }

        public IDataProviderTracker Tracker { get; }

        public IDataProviderStats Stats { get; }

        public string? FileTransferUrl { get; }
    }
}
