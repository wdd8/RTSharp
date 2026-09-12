using Grpc.Core;

using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Utils;

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace RTSharp.Shared.Abstractions.Daemon;

/// <summary>
/// Hash to compute over a remote file block, along with any parameters that hash needs
/// </summary>
public abstract record FileBlockHashAlgorithm
{
    private FileBlockHashAlgorithm() { }

    /// <summary>
    /// Flat SHA-1 over the block
    /// </summary>
    public sealed record Sha1 : FileBlockHashAlgorithm;

    /// <summary>
    /// BitTorrent v2 merkle root over the block's 16 KiB SHA-256 leaves
    /// </summary>
    /// <param name="Height">Height of the merkle subtree from the block's 16 KiB leaves up to the produced root</param>
    public sealed record Sha256BitTorrentV2Merkle(int Height) : FileBlockHashAlgorithm;
}

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
    Task RequestReceiveFiles(IEnumerable<(string RemoteSource, string StoreTo, ulong TotalSize)> Paths, string SenderServerId, Action<(string File, float Progress)> Progress);

    Task<MemoryStream> ReceiveFilesInline(string RemotePath);

    /// <summary>
    /// Compiles and runs a script on the server
    /// </summary>
    /// <param name="Input">Messages to push into the script before it starts</param>
    Task<Guid> RunCustomScript(string Script, string Name, IReadOnlyList<ScriptMessage> Input);

    /// <summary>
    /// Installs a script the server keeps and starts again on every restart
    /// </summary>
    /// <param name="Id">Constant identifier per unique script</param>
    /// <param name="OwnerId">GUID of the owning plugin, or <see cref="Guid.Empty"/> for the application</param>
    /// <param name="Input">Messages to push into the script before it starts</param>
    Task<InstalledScript> InstallScript(string Id, string Name, Guid OwnerId, string Script, IReadOnlyList<ScriptMessage> Input);

    /// <summary>
    /// Stops an installed script and removes it from the server
    /// </summary>
    Task UninstallScript(string Id);

    /// <summary>
    /// Lists installed scripts
    /// </summary>
    Task<IReadOnlyList<InstalledScript>> ListInstalledScripts();

    /// <summary>
    /// Records on the server that a user has been asked about this script being orphaned and chose
    /// to keep it
    /// </summary>
    Task AcknowledgeOrphanedScript(string Id);

    Task QueueScriptCancellation(Guid Id);

    Task RemoveScriptSession(Guid Id);
    
    Task GetScriptProgress(Guid Req, Action<ScriptProgressState>? Progress);

    /// <summary>
    /// Attaches to a script session's message channel
    /// </summary>
    IScriptChannel OpenScriptChannel(Guid SessionId, CancellationToken CancellationToken);

    /// <summary>
    /// Gets script session updates from the server.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <param name="includeFullUpdate">Emit the current script sessions before live updates.</param>
    /// <param name="streamUpdates">Keep streaming live updates after the initial populate.</param>
    /// <returns></returns>
    ChannelReader<ScriptSessionUpdate> StreamScriptsStatus(CancellationToken cancellationToken, bool includeFullUpdate = true, bool streamUpdates = true);

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


    /// <summary>
    /// Hashes a file block using a specified hash algorithm
    /// </summary>
    /// <param name="Path">Full remote path</param>
    /// <param name="Start">Start offset</param>
    /// <param name="End">End offset</param>
    /// <param name="HashAlgorithm">Flat hash, or a BitTorrent v2 merkle root, carrying its own parameters</param>
    /// <returns></returns>
    Task<byte[]> HashFileBlock(string Path, ulong Start, ulong End, FileBlockHashAlgorithm HashAlgorithm);

    /// <summary>
    /// Links files from source to destination using either reflink, hardlink, or reflink with hardlink fallback. There is no hardlink with reflink fallback.
    /// </summary>
    /// <param name="SrcToDst">Paths</param>
    /// <param name="Reflink"></param>
    /// <param name="Hardlink"></param>
    /// <returns></returns>
    Task LinkFiles(List<KeyValuePair<string, string>> SrcToDst, bool Reflink, bool Hardlink);

    T GetGrpcService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : ClientBase<T>;

    IDaemonTorrentsService GetTorrentsService(IDataProvider DataProvider);

    IDaemonStatsService GetStatsService(IDataProvider DataProvider);

    /// <summary>
    /// Streams the logs of data provider daemon
    /// </summary>
    /// <param name="DataProvider">Data provider whose daemon logs to stream.</param>
    /// <param name="History">Number of recent lines to backfill</param>
    /// <param name="CancellationToken"></param>
    ChannelReader<DataProviderDaemonLogEntry> StreamLogs(IDataProvider DataProvider, int History, CancellationToken CancellationToken);

    string Id { get; }

    string Host { get; }

    ushort Port { get; }

    Uri Uri { get; }
}
