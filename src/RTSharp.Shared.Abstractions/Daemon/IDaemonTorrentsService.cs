using System.Threading.Channels;
using RTSharp.Shared.Utils;

namespace RTSharp.Shared.Abstractions.Daemon
{
    public interface IDaemonTorrentsService
    {
        Channel<ListingChanges<Torrent, byte[]>> GetTorrentChanges(CancellationToken CancellationToken);
        
        Task<TorrentStatuses> RemoveTorrentsAndData(IList<byte[]> Hashes);
        
        Task<InfoHashDictionary<IList<Tracker>>> GetTorrentsTrackers(IList<byte[]> Hashes);
        
        Task<InfoHashDictionary<byte[]>> GetDotTorrents(IList<byte[]> Hashes);
    }
}