using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Shared.Utils;

using System.Threading.Channels;

namespace RTSharp.Shared.Abstractions.Daemon;

public interface IDaemonTorrentsService
{
    Channel<ListingChanges<Torrent, T, byte[]>> GetTorrentChanges<T>(ConcurrentInfoHashOwnerDictionary<T> Existing, Action<IncompleteDeltaTorrentResponse, T> Update, Action<CompleteDeltaTorrentResponse, T> Update2, CancellationToken CancellationToken)
            where T : class;

    Task<TorrentStatuses> RemoveTorrentsAndData(IList<Torrent> In);
    
    Task<InfoHashDictionary<IList<Tracker>>> GetTorrentsTrackers(IList<Torrent> In, CancellationToken CancellationToken);
    
    Task<InfoHashDictionary<byte[]>> GetDotTorrents(IList<Torrent> In);
    
    Task<InfoHashDictionary<IList<Peer>>> GetTorrentsPeers(IList<Torrent> In, CancellationToken cancellationToken);
    
    Task<InfoHashDictionary<(bool MultiFile, IList<File> Files)>> GetTorrentsFiles(IList<Torrent> In, CancellationToken cancellationToken);
    
    Task<IEnumerable<Torrent>> GetAllTorrents(CancellationToken cancellationToken);
    
    Task<TorrentStatuses> StartTorrents(IList<byte[]> Hashes);
    
    Task<TorrentStatuses> PauseTorrents(IList<byte[]> Hashes);
    
    Task<TorrentStatuses> StopTorrents(IList<byte[]> Hashes);
    
    Task<Guid> ForceRecheckTorrents(IList<byte[]> Hashes);
    
    Task<TorrentStatuses> AddTorrents(IList<(byte[] Data, string? Filename, AddTorrentsOptions Options)> In);
    
    Task<TorrentStatuses> ReannounceToAllTrackers(IList<byte[]> Hashes);
    
    Task<TorrentStatuses> RemoveTorrents(IList<byte[]> Hashes);
    
    Task<TorrentStatuses> SetLabels(IList<(byte[] Hash, string[] Labels)> In);
    
    Task<InfoHashDictionary<IList<PieceState>>> GetPieces(IList<Torrent> In, CancellationToken cancellationToken);

    Task<string?> MoveDownloadDirectoryPreCheck(InfoHashDictionary<string> Torrents, IList<(string SourceFile, string TargetFile)> Check, bool Move);

    Task<Guid> MoveDownloadDirectory(InfoHashDictionary<string> Torrents, bool Move, bool DeleteSourceFiles);

    Task<Torrent> GetTorrent(byte[] Hash);

    Task QueueTorrentUpdate(IList<byte[]> Hashes);

    Task ReplaceTracker(byte[] Hash, string Existing, string New, CancellationToken cancellationToken);
}