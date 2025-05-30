using Grpc.Core;
using RTSharp.Shared.Utils;

namespace RTSharp.Shared.Abstractions.Daemon;

[Singleton]
public interface IDaemonService
{
    public Notifyable<TimeSpan> Latency { get; }

    /// <summary>
    /// Creates a new empty directory
    /// </summary>
    /// <param name="Path">Absolute path to new directory</param>
    Task CreateDirectory(string Path);

    /// <summary>
    /// Checks if files exist
    /// </summary>
    /// <param name="Files">Absolute paths to files</param>
    Task<Dictionary<string, bool>> CheckExists(IList<string> Files);

    /// <summary>
    /// Gets directory info and its children (non-recursive)
    /// </summary>
    /// <param name="Path">Absolute path to directory</param>
    Task<FileSystemItem> GetDirectoryInfo(string Path);

    /// <summary>
    /// Invokes mediainfo for provided files
    /// </summary>
    /// <param name="Files">Absolute paths to files</param>
    /// <returns>mediainfo output</returns>
    Task<IList<string>> Mediainfo(IList<string> Files);

    /// <summary>
    /// Checks if daemon connection is possible
    /// </summary>
    Task Ping(CancellationToken cancellationToken);

    /// <summary>
    /// Removes directory if its empty
    /// </summary>
    /// <param name="Path">Absolute path to directory</param>
    Task RemoveEmptyDirectory(string Path);

    /// <summary>
    /// Instruct daemon to connect to remote <paramref name="SenderServerId"/> and receive requested files
    /// </summary>
    /// <param name="Paths">Absolute paths of remote source <c>RemoteSource</c> and destination target <c>StoreTo</c></param>
    /// <param name="SenderServerId">Server where files are present</param>
    /// <param name="Progress">Progress for each file</param>
    Task RequestReceiveFiles(IEnumerable<(string RemoteSource, string StoreTo, ulong TotalSize)> Paths, string SenderServerId, IProgress<(string File, float Progress)> Progress);

    Task<MemoryStream> ReceiveFilesInline(string RemotePath);

    Task<Guid> RunCustomScript(string Script, string Name, Dictionary<string, string> Variables);

    Task QueueScriptCancellation(Guid Id);
    
    Task GetScriptProgress(Guid Req, IProgress<ScriptProgressState>? Progress);
    
    /// <summary>
    /// Checks files permissions to see if it's possible to delete provided files
    /// </summary>
    /// <param name="In">Files</param>
    /// <returns>Is allowed for each file</returns>
    Task<Dictionary<string, bool>> AllowedToDeleteFiles(IEnumerable<string> In);
    
    /// <summary>
    /// Checks files permissions to see if it's possible to read provided files
    /// </summary>
    /// <param name="In">Files</param>
    /// <returns>Is allowed for each file</returns>
    Task<Dictionary<string, bool>> AllowedToReadFiles(IEnumerable<string> In);

    T GetGrpcService<T>()
        where T : ClientBase<T>;

    IDaemonTorrentsService GetTorrentsService(IDataProvider DataProvider);

    string Id { get; }

    string Host { get; }

    ushort Port { get; }
}