using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Utils;

using System.Threading.Channels;

namespace RTSharp.Shared.Abstractions
{
    public interface IDataProvider : IDataProviderBase<DataProviderCapabilities>
    {
        public Task<IEnumerable<Torrent>> GetAllTorrents(CancellationToken cancellationToken);

        public Task<Torrent> GetTorrent(byte[] Hash);

        Task<ChannelReader<ListingChanges<Torrent, T, byte[]>>> GetTorrentChanges<T>(ConcurrentInfoHashOwnerDictionary<T> Existing, Action<IncompleteDeltaTorrentResponse, T> Update, Action<CompleteDeltaTorrentResponse, T> Update2, CancellationToken CancellationToken)
            where T : class;

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

        public IDataProviderFiles Files { get; }

        public IDataProviderTracker Tracker { get; }

        public IDataProviderStats Stats { get; }
    }
}
