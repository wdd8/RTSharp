using BencodeNET.Parsing;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using RTSharp.Daemon.GRPCServices.DataProvider;
using RTSharp.Daemon.Primitives;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Shared.Utils;

using System.Collections.Concurrent;
using System.Diagnostics;

using ISpeedMovingAverage = RTSharp.Shared.Abstractions.ISpeedMovingAverage;
using ISpeedMovingAverageService = RTSharp.Shared.Abstractions.ISpeedMovingAverageService;
using ScriptProgressState = RTSharp.Shared.Abstractions.ScriptProgressState;
using TASK_STATE = RTSharp.Shared.Abstractions.TASK_STATE;

namespace RTSharp.Daemon.Services;

public class TorrentOpService
{
    /// <summary>
    /// Where a file goes when duplicating a torrent
    /// </summary>
    /// <param name="SourcePath">Absolute path on the source server</param>
    /// <param name="RelativeTarget">Path relative to the target directory</param>
    /// <param name="Size">File size</param>
    public record FilePlacement(string SourcePath, string RelativeTarget, ulong Size);

    private readonly SessionsService Sessions;
    private readonly ChannelsService Channels;
    private readonly FileTransferService FileTransfer;
    private readonly ILogger<TorrentOpService> Logger;
    private readonly ISpeedMovingAverageService Smas;

    public TorrentOpService(SessionsService Sessions, ChannelsService Channels, FileTransferService FileTransfer, ILogger<TorrentOpService> Logger, ISpeedMovingAverageService Smas, IServiceProvider Provider)
    {
        this.Sessions = Sessions;
        this.Channels = Channels;
        this.FileTransfer = FileTransfer;
        this.Logger = Logger;
        this.Smas = Smas;
        this.Remote = ActivatorUtilities.CreateInstance<RemoteOps>(Provider, this);
    }
    public async Task<Torrent> GetTorrent(RegisteredDataProvider RegisteredDataProvider, byte[] Hash)
    {
        var req = Hash.ToBytesValue();
        return RegisteredDataProvider.Type switch {
            DataProviderType.rtorrent => await RegisteredDataProvider.Resolve<Services.rtorrent.Grpc>().GetTorrent(req),
            DataProviderType.qbittorrent => await RegisteredDataProvider.Resolve<Services.qbittorrent.Grpc>().GetTorrent(req),
            DataProviderType.transmission => await RegisteredDataProvider.Resolve<Services.transmission.Grpc>().GetTorrent(req),
            _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
        };
    }

    /// <summary>
    /// Waits until a torrent is completed
    /// </summary>
    /// <exception cref="InvalidOperationException">Completed, but with files that weren't wanted, so not all of them are there</exception>
    public async Task WaitForCompletion(RegisteredDataProvider RegisteredDataProvider, byte[] Hash, CancellationToken CancellationToken)
    {
        Logger.LogDebug($"{RegisteredDataProvider}: Waiting for torrent {Convert.ToHexString(Hash)} to complete");

        var lastActivity = Stopwatch.StartNew();
        ulong? lastCompletedSize = null;

        while (true) {
            CancellationToken.ThrowIfCancellationRequested();

            Torrent torrent;
            try {
                torrent = await GetTorrent(RegisteredDataProvider, Hash);
            } catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound) {
                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
                continue;
            }

            if (torrent.IsCompleted) {
                // WantedSize can't tell, as qbittorrent leaves padding out of it but not out of Size
                var files = await GetTorrentsFiles(RegisteredDataProvider, new Torrents { Hashes = { Hash.ToByteString() } });
                if (files.Reply.Any(x => x.Files.Any(f => f.Priority == TorrentsFilesReply.Types.FilePriority.DontDownload)))
                    throw new InvalidOperationException($"Torrent {torrent.Name} isn't fully downloaded, as not all of its files are wanted");

                return;
            }

            if (torrent.CompletedSize != lastCompletedSize) {
                lastCompletedSize = torrent.CompletedSize;
                lastActivity.Restart();
            } else if (torrent.State.HasFlag(TorrentState.Downloading) && lastActivity.Elapsed >= TimeSpan.FromMinutes(1)) {
                throw new RpcException(new Grpc.Core.Status(StatusCode.DeadlineExceeded, $"Torrent {Convert.ToHexString(Hash)} made no progress for 1 minute while downloading"));
            }

            var delay = torrent.ETA?.Seconds < 10 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(5);
            await Task.Delay(delay, CancellationToken);
        }
    }

    public async Task<TorrentsFilesReply> GetTorrentsFiles(RegisteredDataProvider RegisteredDataProvider, Torrents Req)
    {
        return RegisteredDataProvider.Type switch {
            DataProviderType.rtorrent => await RegisteredDataProvider.Resolve<Services.rtorrent.Grpc>().GetTorrentsFiles(Req),
            DataProviderType.qbittorrent => await RegisteredDataProvider.Resolve<Services.qbittorrent.Grpc>().GetTorrentsFiles(Req),
            DataProviderType.transmission => await RegisteredDataProvider.Resolve<Services.transmission.Grpc>().GetTorrentsFiles(Req),
            _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
        };
    }

    public Task<BytesValue> ForceRecheckTorrents(RegisteredDataProvider RegisteredDataProvider, Torrents Req, CancellationToken CancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var session = Sessions.RunSession("Force recheck torrents", cts, session => ForceRecheckTorrents(RegisteredDataProvider, Req, session.Progress, cts.Token));

        return Task.FromResult(new BytesValue {
            Value = session.Id.ToByteString()
        });
    }

    public Task ForceRecheckTorrents(RegisteredDataProvider RegisteredDataProvider, Torrents Req, ScriptProgressState Progress, CancellationToken CancellationToken)
    {
        return RegisteredDataProvider.Type switch {
            DataProviderType.rtorrent => RegisteredDataProvider.Resolve<Services.rtorrent.Grpc>().ForceRecheckTorrents(Req, Progress, CancellationToken),
            DataProviderType.qbittorrent => RegisteredDataProvider.Resolve<Services.qbittorrent.Grpc>().ForceRecheckTorrents(Req, Progress, CancellationToken),
            DataProviderType.transmission => RegisteredDataProvider.Resolve<Services.transmission.Grpc>().ForceRecheckTorrents(Req, Progress, CancellationToken),
            _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
        };
    }

    public async Task<TorrentsReply> TryStartSeeding(RegisteredDataProvider RegisteredDataProvider, Torrents Req, CancellationToken CancellationToken)
    {
        var okHashes = new ConcurrentBag<ByteString>();

        Logger.LogDebug($"{RegisteredDataProvider}: Trying to start seeding {Req.Hashes.Count} torrents");

        var tasks = Req.Hashes.Select(async x => {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 10) {
                Torrent? torrent;
                try {
                    torrent = await GetTorrent(RegisteredDataProvider, x.ToByteArray());
                } catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound) {
                    torrent = null;
                }

                if (torrent == null || !torrent.IsCompleted) {
                    await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
                    continue;
                }

                okHashes.Add(x);
                return;
            }
            Logger.LogWarning($"Was instructed to start seeding torrent {Convert.ToHexString(x.ToByteArray())} but it doesn't exist or is not fully downloaded");
        });

        await Task.WhenAll(tasks);

        return await StartTorrents(RegisteredDataProvider, new Torrents {
            Hashes = { okHashes }
        });
    }

    public Task<TorrentsReply> StartTorrents(RegisteredDataProvider RegisteredDataProvider, Torrents Req)
    {
        return RegisteredDataProvider.Type switch {
            DataProviderType.rtorrent => RegisteredDataProvider.Resolve<Services.rtorrent.Grpc>().StartTorrents(Req),
            DataProviderType.qbittorrent => RegisteredDataProvider.Resolve<Services.qbittorrent.Grpc>().StartTorrents(Req),
            DataProviderType.transmission => RegisteredDataProvider.Resolve<Services.transmission.Grpc>().StartTorrents(Req),
            _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
        };
    }

    public Task<Stream> GetDotTorrentStream(RegisteredDataProvider RegisteredDataProvider, byte[] Hash, CancellationToken CancellationToken)
    {
        var hash = Hash.ToByteString();
        return RegisteredDataProvider.Type switch {
            DataProviderType.rtorrent => RegisteredDataProvider.Resolve<Services.rtorrent.Grpc>().GetDotTorrentStream(hash, CancellationToken),
            DataProviderType.qbittorrent => RegisteredDataProvider.Resolve<Services.qbittorrent.Grpc>().GetDotTorrentStream(hash, CancellationToken),
            DataProviderType.transmission => RegisteredDataProvider.Resolve<Services.transmission.Grpc>().GetDotTorrentStream(hash, CancellationToken),
            _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
        };
    }

    public async Task<TorrentsReply> AddTorrent(RegisteredDataProvider RegisteredDataProvider, Stream Data, string Path, string Filename, bool FastResume)
    {
        static async IAsyncEnumerable<NewTorrentsData> buildAddMessages(Stream Data, string Path, string Filename, bool FastResume)
        {
            yield return new NewTorrentsData {
                TMetadata = new NewTorrentsData.Types.Metadata {
                    Id = "0",
                    Path = Path,
                    Filename = Filename,
                    FastResume = FastResume
                }
            };
            int sz;
            var buf = new byte[65536];

            while ((sz = await Data.ReadAsync(buf)) > 0) {
                yield return new NewTorrentsData {
                    TData = new NewTorrentsData.Types.TorrentData {
                        Id = "0",
                        Chunk = ByteString.CopyFrom(buf, 0, sz)
                    }
                };
            }
        }

        Logger.LogDebug($"{RegisteredDataProvider}: Adding torrent {Filename} (basepath {Path}){(FastResume ? " with fast resume" : "")}");

        var msgs = buildAddMessages(Data, Path, Filename, FastResume);
        return await (RegisteredDataProvider.Type switch {
            DataProviderType.rtorrent => RegisteredDataProvider.Resolve<Services.rtorrent.Grpc>().AddTorrents(msgs),
            DataProviderType.qbittorrent => RegisteredDataProvider.Resolve<Services.qbittorrent.Grpc>().AddTorrents(msgs),
            DataProviderType.transmission => RegisteredDataProvider.Resolve<Services.transmission.Grpc>().AddTorrents(msgs),
            _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
        });
    }

    public ScriptSession ReceiveTorrentData(RegisteredDataProvider TargetDp, ReceiveTorrentDataInput Req)
    {
        var cts = new CancellationTokenSource();
        return Sessions.RunSession("Receive torrent data", cts, async session => {
            session.Progress.State = TASK_STATE.RUNNING;

            try {
                foreach (var f in Req.Files) {
                    if (Path.IsPathRooted(f.StorePath))
                        throw new InvalidOperationException($"Torrent file path is rooted: {f.StorePath}");
                }

                var transfer = new ScriptProgressState(session) { Text = "Receiving files...", Progress = 0f, State = TASK_STATE.RUNNING };
                session.Progress.Chain = [transfer];

                var filesClient = Channels.GetFilesClient(Req.SourceUri);

                var totalBytes = Req.Files.Sum(f => (long)f.Size);
                long completedBytes = 0;
                long currentFileTotalBytes = 0;
                string? currentPath = null;

                await FileTransfer.ReceiveFilesFromRemote(filesClient, Req.Files.Select(f => (
                    StorePath: Path.Combine(Req.TargetPath, f.StorePath),
                    RemoteSourcePath: f.SourcePath
                )), progress => {
                    if (progress.Path == "") {
                        transfer.Progress = 100f;
                        return;
                    }

                    if (progress.Path != currentPath) {
                        completedBytes += currentFileTotalBytes;
                        currentFileTotalBytes = progress.TotalBytes;
                        currentPath = progress.Path;
                    }

                    var doneBytes = completedBytes + progress.BytesReceived;
                    transfer.Progress = totalBytes > 0 ? doneBytes / (float)totalBytes * 100f : 0f;
                    transfer.Text = progress.BytesPerSecond > 0 ? $"{Converters.GetSIDataSize((ulong)progress.BytesPerSecond)}/s" : "Receiving files...";
                });
                transfer.State = TASK_STATE.DONE;
                session.Progress.State = TASK_STATE.DONE;
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                // Set here, as a status stream can end before the session's continuation marks it as failed
                session.Progress.Text = ex.Message;
                session.Progress.State = TASK_STATE.FAILED;
                throw;
            }
        });
    }

    /// <summary>
    /// Resolves where the files of torrents go when duplicating them
    /// </summary>
    /// <param name="SourceDp">Data provider of the torrents</param>
    /// <param name="Hashes">Info hashes of the torrents. Only <paramref name="TargetTorrent"/> can bring the files of several together</param>
    /// <param name="TargetTorrent">.torrent of the same content to lay the files out for, or <c>null</c> to keep the layout of the torrent</param>
    /// <param name="CancellationToken"></param>
    public async Task<FilePlacement[]> GetFilePlacements(RegisteredDataProvider SourceDp, byte[][] Hashes, byte[]? TargetTorrent, CancellationToken CancellationToken)
    {
        if (TargetTorrent == null) {
            var hash = Hashes.Single();
            var source = await GetTorrent(SourceDp, hash);
            var filesReply = await GetTorrentsFiles(SourceDp, new Torrents { Hashes = { hash.ToByteString() } });
            var files = filesReply.Reply.FirstOrDefault(x => x.InfoHash.ToByteArray().SequenceEqual(hash)) ?? throw new InvalidOperationException($"No file listing returned for hash {Convert.ToHexString(hash)}");
            var prefix = files.MultiFile ? source.Name : "";

            return [ .. files.Files.Select(f => new FilePlacement(Path.Combine(source.RemotePath, prefix, f.Path), Path.Combine(prefix, f.Path), f.Size)) ];
        }

        var parser = new BencodeParser();

        var onDisk = new List<(BencodeNET.Objects.BDictionary Info, string BaseDirectory)>(Hashes.Length);

        foreach (var hash in Hashes) {
            var source = await GetTorrent(SourceDp, hash);

            using var dotTorrent = new MemoryStream();
            using (var stream = await GetDotTorrentStream(SourceDp, hash, CancellationToken)) {
                await stream.CopyToAsync(dotTorrent, CancellationToken);
                dotTorrent.Position = 0;
            }

            var sourceInfo = parser.Parse<BencodeNET.Objects.BDictionary>(dotTorrent).Get<BencodeNET.Objects.BDictionary>("info") ?? throw new InvalidDataException("Torrent has no \"info\" dictionary");

            onDisk.Add((sourceInfo, source.RemotePath));
        }

        using var targetTorrent = new MemoryStream(TargetTorrent);
        var info = parser.Parse<BencodeNET.Objects.BDictionary>(targetTorrent).Get<BencodeNET.Objects.BDictionary>("info")
            ?? throw new InvalidDataException("Torrent has no \"info\" dictionary");

        if (!BencodeUtils.HasSafePaths(info))
            throw new InvalidOperationException("Unsafe file paths");

        var match = BencodeUtils.MatchFileTrees(info, onDisk) ?? throw new InvalidOperationException("The files of the torrents can't be paired");

        return [ .. BencodeUtils.GetFilesOnDisk(info, "").Select((file, x) => new FilePlacement(match.DiskFiles[x].Path, file.Path, (ulong)file.FileSize)) ];
    }

    public async Task TransmitTorrent(byte[] Hash, DataProviderEndpoint Target, string SourceUri, string TargetPath, IReadOnlyList<FilePlacement> Files, CancellationToken CancellationToken)
    {
        // TODO: SourceUri should ideally be known by the server as it has the data provider registered in it.. but if we are listening on 0.0.0.0, what's our URI?
        ArgumentException.ThrowIfNullOrEmpty(Target.ProviderKey, nameof(Target.ProviderKey));
        ArgumentException.ThrowIfNullOrEmpty(Target.Uri, nameof(Target.Uri));
        ArgumentException.ThrowIfNullOrEmpty(SourceUri, nameof(SourceUri));
        ArgumentException.ThrowIfNullOrEmpty(TargetPath, nameof(TargetPath));

        Logger.LogDebug($"Transmitting {Convert.ToHexStringLower(Hash)} to {Target.Uri} ({Target.ProviderKey}) -> remote {TargetPath}");

        var targetClient = Channels.GetTorrentServiceClient(Target.Uri);
        var headers = new CallOptions(headers: new Metadata { { "data-provider", Target.ProviderKey } });

        var session = await targetClient.ReceiveTorrentDataAsync(new ReceiveTorrentDataInput {
            Hash = Hash.ToByteString(),
            SourceUri = SourceUri,
            Files = { Files.Select(f => new ReceiveTorrentDataInput.Types.FileEntry { SourcePath = f.SourcePath, Size = f.Size, StorePath = f.RelativeTarget }) },
            TargetPath = TargetPath
        }, headers);

        var serverClient = Channels.GetServerClient(Target.Uri);
        var progress = serverClient.ScriptStatus(session, cancellationToken: CancellationToken);
        await foreach (var info in progress.ResponseStream.ReadAllAsync(CancellationToken)) {
            if (info.State == Protocols.TaskState.Done) {
                Logger.LogDebug($"Transmission of {Convert.ToHexStringLower(Hash)} to {Target.Uri} ({Target.ProviderKey}) -> remote {TargetPath} done");
                return;
            }
            if (info.State == Protocols.TaskState.Failed)
                throw new InvalidOperationException($"Receiving torrent data failed on target: {info.Text}");
        }

        throw new InvalidOperationException("Receiving torrent data on target ended before it finished");
    }

    public async Task LinkFiles(string TargetPath, Protocols.LinkFilesInput.Types.LinkType Type, IReadOnlyList<FilePlacement> Files, ScriptProgressState Progress, CancellationToken CancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(TargetPath, nameof(TargetPath));

        Logger.LogDebug($"Linking {Files.Count} files -> {TargetPath}");

        var created = new List<string>(Files.Count);

        var totalBytes = Files.Sum(f => (long)f.Size);
        long completedBytes = 0;
        var sw = Stopwatch.StartNew();

        try {
            foreach (var f in Files) {
                CancellationToken.ThrowIfCancellationRequested();

                var dst = Path.Combine(TargetPath, f.RelativeTarget);

                // Either the source itself or from an earlier run. Fast resume verifies the content when it's added
                if (File.Exists(dst)) {
                    completedBytes += (long)f.Size;
                    continue;
                }

                ISpeedMovingAverage? speed = null;

                // Don't like this reference but what can you do
                await GRPCServices.FilesService.LinkFile(f.SourcePath, dst, Type, copied => {
                    if (speed == null) {
                        speed = Smas.Get("FileTransfer", Guid.NewGuid().ToString(), itemsMax: 10);
                        speed.Value(copied);
                    }

                    if (sw.Elapsed <= TimeSpan.FromSeconds(0.5))
                        return;

                    speed.ValueIfChanged(copied);
                    var bytesPerSecond = Math.Max(0, speed.CalculateSpeed());

                    Progress.Progress = totalBytes > 0 ? (completedBytes + copied) / (float)totalBytes * 100f : 0f;
                    Progress.Text = bytesPerSecond > 0 ? $"{Converters.GetSIDataSize((ulong)bytesPerSecond)}/s" : "Copying files...";
                    sw.Restart();
                }, CancellationToken);

                created.Add(dst);
                completedBytes += (long)f.Size;
            }

            Progress.Progress = 100f;
        } catch {
            // The target can be shared with other torrents, so only what this run made is removed
            foreach (var path in created)
                File.Delete(path);

            throw;
        }
    }

    public RemoteOps Remote { get; }

    public class RemoteOps(TorrentOpService Local, SessionsService Sessions, ChannelsService Channels, FileTransferService FileTransfer, ILogger<RemoteOps> Logger)
    {
        public async Task ForceRecheckTorrents(DataProviderEndpoint Target, byte[] Hash, ScriptProgressState Progress, CancellationToken CancellationToken)
        {
            var targetClient = Channels.GetTorrentServiceClient(Target.Uri);
            var headers = new CallOptions(headers: new Metadata { { "data-provider", Target.ProviderKey } });

            var session = await targetClient.ForceRecheckTorrentsAsync(new Torrents { Hashes = { Hash.ToByteString() } }, headers);

            var serverClient = Channels.GetServerClient(Target.Uri);
            var progress = serverClient.ScriptStatus(session, cancellationToken: CancellationToken);
            await foreach (var info in progress.ResponseStream.ReadAllAsync(CancellationToken)) {
                if (info.State == Protocols.TaskState.Done)
                    return;
                if (info.State == Protocols.TaskState.Failed)
                    throw new InvalidOperationException($"Force recheck failed on target: {info.Text}");
            }
        }

        public async Task<TorrentsReply> AddTorrent(DataProviderEndpoint Target, Stream Data, string Path, string Filename, bool FastResume, CancellationToken CancellationToken)
        {
            var targetClient = Channels.GetTorrentServiceClient(Target.Uri);
            var opts = new CallOptions(headers: new Metadata { { "data-provider", Target.ProviderKey } }, cancellationToken: CancellationToken);

            using var call = targetClient.AddTorrents(opts);

            await call.RequestStream.WriteAsync(new NewTorrentsData {
                TMetadata = new NewTorrentsData.Types.Metadata {
                    Id = "0",
                    Path = Path,
                    Filename = Filename,
                    FastResume = FastResume
                }
            });

            var buf = new byte[65536];
            int sz;
            while ((sz = await Data.ReadAsync(buf, CancellationToken)) > 0) {
                await call.RequestStream.WriteAsync(new NewTorrentsData {
                    TData = new NewTorrentsData.Types.TorrentData {
                        Id = "0",
                        Chunk = ByteString.CopyFrom(buf, 0, sz)
                    }
                });
            }

            await call.RequestStream.CompleteAsync();
            return await call.ResponseAsync;
        }

        public async Task<TorrentsReply> TryStartSeeding(DataProviderEndpoint Target, byte[] Hash, CancellationToken CancellationToken)
        {
            var targetClient = Channels.GetTorrentServiceClient(Target.Uri);
            var opts = new CallOptions(headers: new Metadata { { "data-provider", Target.ProviderKey } }, cancellationToken: CancellationToken);

            var ret = await targetClient.TryStartSeedingAsync(new Torrents { Hashes = { Hash.ToByteString() } }, opts);
            return ret;
        }

        public async Task LinkFiles(DataProviderEndpoint Target, string SourcePath, string TargetPath, Protocols.LinkFilesInput.Types.LinkType Type, IReadOnlyList<FilePlacement> Files, CancellationToken CancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(SourcePath, nameof(SourcePath));
            ArgumentException.ThrowIfNullOrEmpty(TargetPath, nameof(TargetPath));

            if (SourcePath == TargetPath)
                return;

            Logger.LogDebug($"Linking {Files.Count} files on {Target.Uri} from {SourcePath} -> {TargetPath}");

            var input = new Protocols.LinkFilesInput {
                Type = Type
            };
            foreach (var f in Files) {
                input.Source.Add(Path.Combine(SourcePath, f.RelativeTarget));
                input.Target.Add(Path.Combine(TargetPath, f.RelativeTarget));
            }

            var filesClient = Channels.GetFilesClient(Target.Uri);
            await filesClient.LinkFilesAsync(input, cancellationToken: CancellationToken);
        }
    }
}
