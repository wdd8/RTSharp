using Google.Protobuf;

using Grpc.Core;
using Grpc.Net.Client;

using RTSharp.Daemon.Protocols;
using RTSharp.Shared.Abstractions;

using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;

namespace RTSharp.Daemon.Services;

public class FileTransferService
{
    private const int TRANSFER_QUEUE_BUF_COUNT = 64;

    public readonly record struct FileTransferSessionProgress(string Path, long BytesReceived, long TotalBytes, double BytesPerSecond);

    public ILogger<FileTransferService> Logger { get; }

    private readonly Dictionary<string, GrpcChannel> Channels = new();

    public FileTransferService(ILogger<FileTransferService> Logger, IConfiguration config, ISpeedMovingAverageService Smas)
    {
        this.Logger = Logger;
        this.Config = config;
        this.Smas = Smas;
    }

    private readonly IConfiguration Config;
    private readonly ISpeedMovingAverageService Smas;


    public async Task ReceiveFilesFromRemote(Protocols.GRPCFilesService.GRPCFilesServiceClient Client, IEnumerable<(string StorePath, string RemoteSourcePath)> Paths, Action<FileTransferSessionProgress> Progress)
    {
        foreach (var dir in Paths.Select(x => Path.GetDirectoryName(x.StorePath)).Distinct())
            Directory.CreateDirectory(dir!); // possible that its null but thats consumers problem

        Logger.LogInformation($"ReceiveFilesFromRemote: Request SendFile");

        var paths = Paths.ToDictionary(x => x.RemoteSourcePath, x => x.StorePath);

        using var req = Client.Internal_SendFiles(new SendFilesInput {
            Paths = { Paths.Select(x => x.RemoteSourcePath) }
        });

        var rx = Channel.CreateBounded<FileBuffer>(new BoundedChannelOptions(TRANSFER_QUEUE_BUF_COUNT) {
            SingleReader = true,
            SingleWriter = true
        });

        using var rxCts = new CancellationTokenSource();
        var rxTask = Task.Run(async () => {
            try {
                await foreach (var buffer in req.ResponseStream.ReadAllAsync(rxCts.Token))
                    await rx.Writer.WriteAsync(buffer, rxCts.Token);
                rx.Writer.TryComplete();
            } catch (Exception ex) {
                rx.Writer.TryComplete(ex);
            }
        });

        FileStream? file = null;
        string? currentPath = null;
        long currentTotalBytes = 0;
        ISpeedMovingAverage? speed = null;
        var sw = Stopwatch.StartNew();
        long bytesTotal = 0;

        try {
            await foreach (var buffer in rx.Reader.ReadAllAsync()) {
                if (buffer.DataCase == FileBuffer.DataOneofCase.Header) {
                    if (file != null) {
                        await file.FlushAsync();
                        await file.DisposeAsync();
                        file = null;
                    }

                    if (buffer.Header.Path == "") {
                        // Done
                        Logger.LogInformation($"ReceiveFilesFromRemote: Done");
                        try { Progress(new("", bytesTotal, currentTotalBytes, 0)); } catch { }
                        break;
                    }

                    Logger.LogDebug($"ReceiveFilesFromRemote: file {buffer.Header.Path} -> {paths[buffer.Header.Path]}");
                    file = new FileStream(currentPath = paths[buffer.Header.Path], new FileStreamOptions {
                        Mode = FileMode.OpenOrCreate,
                        Access = FileAccess.Write,
                        Share = FileShare.None,
                        Options = FileOptions.Asynchronous
                    });
                    currentTotalBytes = (long)buffer.Header.TotalBytes;
                    bytesTotal = 0;
                    speed = Smas.Get("FileTransfer", Guid.NewGuid().ToString(), itemsMax: 10);
                    speed.Value(0);
                    sw.Restart();

                    try { Progress(new(currentPath!, bytesTotal, currentTotalBytes, 0)); } catch { }

                    continue;
                } else if (buffer.DataCase == FileBuffer.DataOneofCase.Buffer) {
                    await file!.WriteAsync(buffer.Buffer.Memory);
                    bytesTotal += buffer.Buffer.Length;

                    if (sw.Elapsed > TimeSpan.FromSeconds(0.5)) {
                        speed!.ValueIfChanged(bytesTotal);
                        var bytesPerSecond = Math.Max(0, speed.CalculateSpeed());
                        try { Progress(new(currentPath!, bytesTotal, currentTotalBytes, bytesPerSecond)); } catch { }
                        sw.Restart();
                    }
                }
            }
        } finally {
            rxCts.Cancel();
            if (file != null)
                await file.DisposeAsync();
        }

        await rxTask;
    }

    public async Task SendFile(FileStream File, IServerStreamWriter<FileBuffer> Stream, ulong? BytesToRead = null)
    {
        const int BUFFER_SIZE = 81920; // < LOH boundary

        var tx = Channel.CreateBounded<(byte[] Buffer, int Length)>(new BoundedChannelOptions(TRANSFER_QUEUE_BUF_COUNT) {
            SingleReader = true,
            SingleWriter = true
        });

        using var txCts = new CancellationTokenSource();
        var txTask = Task.Run(async () => {
            try {
                ulong bytesTotal = 0;
                while (true) {
                    int toRead = BUFFER_SIZE;
                    if (BytesToRead.HasValue) {
                        var remaining = BytesToRead.Value - bytesTotal;
                        if (remaining == 0)
                            break;
                        toRead = (int)Math.Min((ulong)toRead, remaining);
                    }

                    var buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
                    int bytesRead;
                    try {
                        bytesRead = await File.ReadAsync(buffer.AsMemory(0, toRead), txCts.Token);
                    } catch {
                        ArrayPool<byte>.Shared.Return(buffer);
                        throw;
                    }
                    if (bytesRead == 0) {
                        ArrayPool<byte>.Shared.Return(buffer);
                        break;
                    }
                    bytesTotal += (ulong)bytesRead;

                    try {
                        await tx.Writer.WriteAsync((buffer, bytesRead), txCts.Token);
                    } catch {
                        ArrayPool<byte>.Shared.Return(buffer);
                        throw;
                    }
                }
                tx.Writer.TryComplete();
            } catch (Exception ex) {
                tx.Writer.TryComplete(ex);
            }
        });

        try {
            await foreach (var (buffer, length) in tx.Reader.ReadAllAsync()) {
                try {
                    await Stream.WriteAsync(new FileBuffer {
                        Buffer = UnsafeByteOperations.UnsafeWrap(buffer.AsMemory(0, length))
                    });
                } finally {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        } finally {
            txCts.Cancel();
            await txTask;
            while (tx.Reader.TryRead(out var item))
                ArrayPool<byte>.Shared.Return(item.Buffer);
        }
    }
}
