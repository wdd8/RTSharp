namespace RTSharp.Daemon.Protocols.DataProvider;

public sealed partial class Torrent
{
    /// <summary>
    /// Is torrent fully downloaded?
    /// </summary>
    public bool IsCompleted => CompletedSize >= WantedSize;
}
