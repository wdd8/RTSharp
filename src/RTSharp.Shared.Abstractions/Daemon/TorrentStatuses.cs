using RTSharp.Shared.Utils;

namespace RTSharp.Shared.Abstractions.Daemon;

public class TorrentStatuses : List<(byte[] Hash, IList<Exception> Exceptions)>
{
}