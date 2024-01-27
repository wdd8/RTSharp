namespace RTSharp.Shared.Abstractions;

public interface IAuxiliaryService
{
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
    /// Checks if auxiliary service connection is possible
    /// </summary>
    Task Ping();

    /// <summary>
    /// Removes directory if its empty
    /// </summary>
    /// <param name="Path">Absolute path to directory</param>
    Task RemoveEmptyDirectory(string Path);

    /// <summary>
    /// Instruct auxiliary service to connect to remote <paramref name="SenderServerId"/> and receive requested files
    /// </summary>
    /// <param name="Paths">Absolute paths of remote source <c>RemoteSource</c> and destination target <c>StoreTo</c></param>
    /// <param name="SenderServerId">Server where files are present</param>
    /// <param name="Progress">Progress for each file</param>
    Task RequestRecieveFiles(IEnumerable<(string RemoteSource, string StoreTo)> Paths, string SenderServerId, IProgress<(string File, ulong BytesTransferred)> Progress);
}