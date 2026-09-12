namespace RTSharp.Scripts.RequestReceiveFiles;

public partial record RequestReceiveFilesInput(string Uri, RequestReceiveFilePath[] Paths);
public partial record RequestReceiveFilePath(string StorePath, string RemoteSourcePath, ulong TotalSize);
