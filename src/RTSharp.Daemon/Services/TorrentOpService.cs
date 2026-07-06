using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using RTSharp.Daemon.GRPCServices.DataProvider;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Shared.Utils;

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
        while (true) {
            CancellationToken.ThrowIfCancellationRequested();

            Torrent torrent;
            try {
                torrent = await GetTorrent(RegisteredDataProvider, Hash);
            } catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound) {
                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
                continue;
            }

            if (torrent.CompletedSize >= torrent.WantedSize)
                return;

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
        var okHashes = new List<ByteString>();

        foreach (var hash in Req.Hashes) {
            var torrent = await GetTorrent(RegisteredDataProvider, hash.ToByteArray());
            if (torrent == null || torrent.WantedSize != torrent.CompletedSize) {
                Logger.LogWarning($"Was instructed to start seeding torrent {Convert.ToHexString(hash.ToByteArray())} but it is not fully downloaded");
                continue;
            }

            okHashes.Add(hash);
        }

        return await StartTorrents(RegisteredDataProvider, new Torrents {
            Hashes = { okHashes }
        });
    }

    public Task<TorrentsReply> StartTorrents(RegisteredDataProvider RegisteredDataProvider, Torrents Req)
    {
        return RegisteredDataProvider.Type switch {
            DataProviderType.rtorrent => RegisteredDataProvider.Resolve<Services.rtorrent.Grpc>().StartTorrents(Req),
            DataProviderType.qbittorrent => RegisteredDataProvider.Resolve<Services.qbittorrent.Grpc>().StartTorrents(Req),
            DataProviderType.transmission => RegisteredDataProvider.Resolve<Services.qbittorrent.Grpc>().StartTorrents(Req),
            _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
        };
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

    public async Task TransmitTorrent(RegisteredDataProvider SourceDp, byte[] Hash, string TargetProviderKey, string TargetUri, string SourceUri, string TargetPath, CancellationToken CancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(TargetProviderKey, nameof(TargetProviderKey));
        ArgumentException.ThrowIfNullOrEmpty(TargetUri, nameof(TargetUri));
        ArgumentException.ThrowIfNullOrEmpty(SourceUri, nameof(SourceUri));
        ArgumentException.ThrowIfNullOrEmpty(TargetPath, nameof(TargetPath));

        var sourceTask = GetTorrent(SourceDp, Hash);
        var filesTask = GetTorrentsFiles(SourceDp, new Torrents { Hashes = { Hash.ToByteString() } });
        await Task.WhenAll(sourceTask, filesTask);

        var source = sourceTask.Result;
        var filesReply = filesTask.Result;
        var files = filesReply.Reply.FirstOrDefault(x => x.InfoHash.ToByteArray().SequenceEqual(Hash)) ?? throw new InvalidOperationException($"No file listing returned for hash {Convert.ToHexString(Hash)}");

        var targetClient = Channels.GetTorrentServiceClient(TargetUri);
        var headers = new CallOptions(headers: new Metadata { { "data-provider", TargetProviderKey } });

        var session = await targetClient.ReceiveTorrentDataAsync(new ReceiveTorrentDataInput {
            Hash = Hash.ToByteString(),
            SourceUri = SourceUri,
            SourceName = source.Name,
            SourceRemotePath = source.RemotePath,
            SourceMultiFile = files.MultiFile,
            Files = { files.Files.Select(f => new ReceiveTorrentDataInput.Types.FileEntry { Path = f.Path, Size = f.Size }) },
            TargetPath = TargetPath
        }, headers);

        var serverClient = Channels.GetServerClient(TargetUri);
        var progress = serverClient.ScriptStatus(session, cancellationToken: CancellationToken);
        await foreach (var info in progress.ResponseStream.ReadAllAsync(CancellationToken)) {
            if (info.State == Protocols.TaskState.Done)
                return;
            if (info.State == Protocols.TaskState.Failed)
                throw new InvalidOperationException($"Receiving torrent data failed on target: {info.Text}");
        }
    }

    public RemoteOps Remote { get; }

    public class RemoteOps(TorrentOpService Local, SessionsService Sessions, ChannelsService Channels, FileTransferService FileTransfer, ILogger<RemoteOps> Logger)
    {
        public async Task ForceRecheckTorrents(string TargetUri, string TargetProviderKey, byte[] Hash, ScriptProgressState Progress, CancellationToken CancellationToken)
        {
            var targetClient = Channels.GetTorrentServiceClient(TargetUri);
            var headers = new CallOptions(headers: new Metadata { { "data-provider", TargetProviderKey } });

            var session = await targetClient.ForceRecheckTorrentsAsync(new Torrents { Hashes = { Hash.ToByteString() } }, headers);

            var serverClient = Channels.GetServerClient(TargetUri);
            var progress = serverClient.ScriptStatus(session, cancellationToken: CancellationToken);
            await foreach (var info in progress.ResponseStream.ReadAllAsync(CancellationToken)) {
                if (info.State == Protocols.TaskState.Done)
                    return;
                if (info.State == Protocols.TaskState.Failed)
                    throw new InvalidOperationException($"Force recheck failed on target: {info.Text}");
            }
        }

        public async Task<TorrentsReply> TryStartSeeding(string TargetUri, string TargetProviderKey, byte[] Hash, CancellationToken CancellationToken)
        {
            var targetClient = Channels.GetTorrentServiceClient(TargetUri);
            var opts = new CallOptions(headers: new Metadata { { "data-provider", TargetProviderKey } }, cancellationToken: CancellationToken);

            var ret = await targetClient.TryStartSeedingAsync(new Torrents { Hashes = { Hash.ToByteString() } }, opts);
            return ret;
        }
    }
}
