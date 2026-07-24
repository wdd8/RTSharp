using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using RTSharp.Daemon.GRPCServices.DataProvider;
using RTSharp.Daemon.Primitives;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Shared.Utils;

using System.Collections.Concurrent;
using System.Diagnostics;

using ScriptProgressState = RTSharp.Shared.Abstractions.ScriptProgressState;
using TASK_STATE = RTSharp.Shared.Abstractions.TASK_STATE;

namespace RTSharp.Daemon.Services;

public class TorrentOpService
{
    private readonly SessionsService Sessions;
    private readonly ChannelsService Channels;
    private readonly FileTransferService FileTransfer;
    private readonly ILogger<TorrentOpService> Logger;

    public TorrentOpService(SessionsService Sessions, ChannelsService Channels, FileTransferService FileTransfer, ILogger<TorrentOpService> Logger, IServiceProvider Provider)
    {
        this.Sessions = Sessions;
        this.Channels = Channels;
        this.FileTransfer = FileTransfer;
        this.Logger = Logger;
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

            if (torrent.IsCompleted)
                return;

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

            foreach (var f in Req.Files) {
                if (Path.IsPathRooted(f.Path))
                    throw new InvalidOperationException($"Torrent file path is rooted: {f.Path}");
            }

            var transfer = new ScriptProgressState(session) { Text = "Receiving files...", Progress = 0f, State = TASK_STATE.RUNNING };
            session.Progress.Chain = [transfer];

            var prefix = Req.SourceMultiFile ? Req.SourceName : "";
            var filesClient = Channels.GetFilesClient(Req.SourceUri);

            var totalBytes = Req.Files.Sum(f => (long)f.Size);
            long completedBytes = 0;
            long currentFileTotalBytes = 0;
            string? currentPath = null;

            await FileTransfer.ReceiveFilesFromRemote(filesClient, Req.Files.Select(f => (
                StorePath: Path.Combine(Req.TargetPath, prefix, f.Path),
                RemoteSourcePath: Path.Combine(Req.SourceRemotePath, prefix, f.Path)
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
        });
    }

    public async Task TransmitTorrent(RegisteredDataProvider SourceDp, byte[] Hash, DataProviderEndpoint Target, string SourceUri, string TargetPath, CancellationToken CancellationToken)
    {
        // TODO: SourceUri should ideally be known by the server as it has the data provider registered in it.. but if we are listening on 0.0.0.0, what's our URI?
        ArgumentException.ThrowIfNullOrEmpty(Target.ProviderKey, nameof(Target.ProviderKey));
        ArgumentException.ThrowIfNullOrEmpty(Target.Uri, nameof(Target.Uri));
        ArgumentException.ThrowIfNullOrEmpty(SourceUri, nameof(SourceUri));
        ArgumentException.ThrowIfNullOrEmpty(TargetPath, nameof(TargetPath));

        Logger.LogDebug($"{SourceDp}: Transmitting {Convert.ToHexStringLower(Hash)} to {Target.Uri} ({Target.ProviderKey}) -> remote {TargetPath}");

        var sourceTask = GetTorrent(SourceDp, Hash);
        var filesTask = GetTorrentsFiles(SourceDp, new Torrents { Hashes = { Hash.ToByteString() } });
        await Task.WhenAll(sourceTask, filesTask);

        var source = sourceTask.Result;
        var filesReply = filesTask.Result;
        var files = filesReply.Reply.FirstOrDefault(x => x.InfoHash.ToByteArray().SequenceEqual(Hash)) ?? throw new InvalidOperationException($"No file listing returned for hash {Convert.ToHexString(Hash)}");

        var targetClient = Channels.GetTorrentServiceClient(Target.Uri);
        var headers = new CallOptions(headers: new Metadata { { "data-provider", Target.ProviderKey } });

        var session = await targetClient.ReceiveTorrentDataAsync(new ReceiveTorrentDataInput {
            Hash = Hash.ToByteString(),
            SourceUri = SourceUri,
            SourceName = source.Name,
            SourceRemotePath = source.RemotePath,
            SourceMultiFile = files.MultiFile,
            Files = { files.Files.Select(f => new ReceiveTorrentDataInput.Types.FileEntry { Path = f.Path, Size = f.Size }) },
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
    }

    public async Task LinkFiles(RegisteredDataProvider SourceDp, byte[] Hash, string TargetPath, CancellationToken CancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(TargetPath, nameof(TargetPath));

        var sourceTask = GetTorrent(SourceDp, Hash);
        var filesTask = GetTorrentsFiles(SourceDp, new Torrents { Hashes = { Hash.ToByteString() } });
        await Task.WhenAll(sourceTask, filesTask);

        var source = sourceTask.Result;
        var files = filesTask.Result.Reply.FirstOrDefault(x => x.InfoHash.ToByteArray().SequenceEqual(Hash)) ?? throw new InvalidOperationException($"No file listing returned for hash {Convert.ToHexString(Hash)}");

        if (source.RemotePath == TargetPath)
            return;

        Logger.LogDebug($"{SourceDp}: Linking {files.Files.Count} files for {Convert.ToHexStringLower(Hash)} from {source.RemotePath} -> {TargetPath}");

        var prefix = files.MultiFile ? source.Name : "";
        foreach (var f in files.Files) {
            CancellationToken.ThrowIfCancellationRequested();
            var src = Path.Combine(source.RemotePath, prefix, f.Path);
            var dst = Path.Combine(TargetPath, prefix, f.Path);

            // Don't like this reference but what can you do
            GRPCServices.FilesService.LinkFile(src, dst, Protocols.LinkFilesInput.Types.LinkType.Reflink | Protocols.LinkFilesInput.Types.LinkType.Hardlink);
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

        public async Task AddTorrent(DataProviderEndpoint Target, Stream Data, string Path, string Filename, bool FastResume, CancellationToken CancellationToken)
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
            await call.ResponseAsync;
        }

        public async Task<TorrentsReply> TryStartSeeding(DataProviderEndpoint Target, byte[] Hash, CancellationToken CancellationToken)
        {
            var targetClient = Channels.GetTorrentServiceClient(Target.Uri);
            var opts = new CallOptions(headers: new Metadata { { "data-provider", Target.ProviderKey } }, cancellationToken: CancellationToken);

            var ret = await targetClient.TryStartSeedingAsync(new Torrents { Hashes = { Hash.ToByteString() } }, opts);
            return ret;
        }

        public async Task LinkFiles(DataProviderEndpoint Target, string SourcePath, string TargetPath, string Name, TorrentsFilesReply.Types.TorrentsFiles Files, CancellationToken CancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(SourcePath, nameof(SourcePath));
            ArgumentException.ThrowIfNullOrEmpty(TargetPath, nameof(TargetPath));

            if (SourcePath == TargetPath)
                return;

            Logger.LogDebug($"Linking {Files.Files.Count} files on {Target.Uri} from {SourcePath} -> {TargetPath}");

            var prefix = Files.MultiFile ? Name : "";
            var input = new Protocols.LinkFilesInput {
                Type = Protocols.LinkFilesInput.Types.LinkType.Reflink | Protocols.LinkFilesInput.Types.LinkType.Hardlink
            };
            foreach (var f in Files.Files) {
                input.Source.Add(Path.Combine(SourcePath, prefix, f.Path));
                input.Target.Add(Path.Combine(TargetPath, prefix, f.Path));
            }

            var filesClient = Channels.GetFilesClient(Target.Uri);
            await filesClient.LinkFilesAsync(input, cancellationToken: CancellationToken);
        }
    }
}
