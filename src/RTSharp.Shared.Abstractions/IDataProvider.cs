using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Utils;

namespace RTSharp.Shared.Abstractions
{
    public interface IDataProvider : IDataProviderBase<DataProviderCapabilities>
    {
        public Task<IEnumerable<Torrent>> GetAllTorrents(CancellationToken cancellationToken);

        public Task<Torrent> GetTorrent(byte[] Hash);

        public Task<System.Threading.Channels.ChannelReader<ListingChanges<Torrent, byte[]>>> GetTorrentChanges(CancellationToken cancellationToken);

        public Task<InfoHashDictionary<IList<Peer>>> GetPeers(IList<Torrent> In, CancellationToken cancellationToken = default);

        public Task<InfoHashDictionary<(bool MultiFile, IList<File> Files)>> GetFiles(IList<Torrent> In, CancellationToken cancellationToken = default);

        public Task<InfoHashDictionary<IList<PieceState>>> GetPieces(IList<Torrent> In, CancellationToken cancellationToken = default);

        public Task<InfoHashDictionary<IList<Tracker>>> GetTrackers(IList<Torrent> In, CancellationToken cancellationToken = default);

        public Task<TorrentStatuses> StartTorrents(IList<byte[]> In);

        public Task<TorrentStatuses> PauseTorrents(IList<byte[]> In);

        public Task<TorrentStatuses> StopTorrents(IList<byte[]> In);

        public Task<TorrentStatuses> AddTorrents(IList<(byte[] Data, string? Filename, AddTorrentsOptions Options)> In);

        public Task<Guid> ForceRecheck(IList<byte[]> In);

        public Task<TorrentStatuses> ReannounceToAllTrackers(IList<byte[]> In);

        public Task<TorrentStatuses> RemoveTorrents(IList<byte[]> In);

        public Task<TorrentStatuses> RemoveTorrentsAndData(IList<Torrent> In);

        public Task<Guid?> MoveDownloadDirectory(InfoHashDictionary<string> In, IList<(string SourceFile, string TargetFile)> Check);

        public Task<InfoHashDictionary<byte[]>> GetDotTorrents(IList<Torrent> In);

        public Task<TorrentStatuses> SetLabels(IList<(byte[] Hash, string[] Labels)> In);

        public IPlugin Plugin { get; }

        public Notifyable<long> TotalDLSpeed { get; }

        public Notifyable<long> TotalUPSpeed { get; }

        public Notifyable<long> ActiveTorrentCount { get; }

        public IDataProviderFiles Files { get; }

        public IDataProviderTracker Tracker { get; }

        public IDataProviderStats Stats { get; }
    }
}
