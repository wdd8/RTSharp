using RTSharp.Shared.Utils;

namespace RTSharp.Shared.Abstractions.Daemon;

public class TorrentStatuses : List<(byte[] Hash, IList<Exception> Exceptions)>
{
    public TorrentStatuses()
    {
    }
    
    public TorrentStatuses(IEnumerable<(byte[] Hash, IList<Exception> Exceptions)> In) : base(In)
    {
    }
}