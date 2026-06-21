using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Utils;

using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace RTSharp.Daemon.Services.FileTail
{
    public static class FileTailReader
    {
        private const int BUFFER_SIZE = 16 * 1024;

        public static async Task StreamPath(string Path, StreamLogsRequest Req, IServerStreamWriter<LogEntry> Res, CancellationToken Ct)
        {
            Path = await PathUtils.ExpandTilde(Path);

            if (!File.Exists(Path))
                throw new RpcException(new Grpc.Core.Status(StatusCode.FailedPrecondition, $"Log file {Path} does not exist"));

            await StreamFile(Path, Req, Res, Ct);
        }

        private static async Task StreamFile(string Path, StreamLogsRequest Req, IServerStreamWriter<LogEntry> Res, CancellationToken Ct)
        {
            if (!File.Exists(Path))
                throw new RpcException(new Grpc.Core.Status(StatusCode.FailedPrecondition, $"Configured log file '{Path}' does not exist"));

            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            long position = stream.Length;

            if (Req.History > 0) {
                foreach (var line in await ReadLastLines(stream, Req.History, Ct))
                    await WriteLineAsync(Res, line, Ct);
            }

            if (!Req.Follow)
                return;

            stream.Seek(position, SeekOrigin.Begin);

            using var signal = new FileChangeSignal(Path);

            var pipe = new Pipe();
            var fill = FillPipeAsync(stream, pipe.Writer, position, signal, Ct);
            var read = ReadPipeAsync(pipe.Reader, Res, Ct);
            await Task.WhenAll(fill, read);
        }

        private static async Task FillPipeAsync(FileStream Stream, PipeWriter Writer, long Position, FileChangeSignal Signal, CancellationToken Ct)
        {
            try {
                while (!Ct.IsCancellationRequested) {
                    var length = Stream.Length;

                    if (length < Position) {
                        Position = 0;
                        Stream.Seek(0, SeekOrigin.Begin);
                    }

                    if (length <= Position) {
                        await Signal.WaitAsync(Ct);
                        continue;
                    }

                    Memory<byte> memory = Writer.GetMemory(BUFFER_SIZE);
                    int read = await Stream.ReadAsync(memory, Ct);
                    if (read == 0) {
                        await Signal.WaitAsync(Ct);
                        continue;
                    }

                    Position += read;
                    Writer.Advance(read);

                    FlushResult flush = await Writer.FlushAsync(Ct);
                    if (flush.IsCompleted)
                        break;
                }
            } catch (OperationCanceledException) {
            } finally {
                await Writer.CompleteAsync();
            }
        }

        private static async Task ReadPipeAsync(PipeReader Reader, IServerStreamWriter<LogEntry> Res, CancellationToken Ct)
        {
            try {
                while (!Ct.IsCancellationRequested) {
                    ReadResult result = await Reader.ReadAsync(Ct);
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                        await WriteLineAsync(Res, DecodeLine(line), Ct);

                    Reader.AdvanceTo(buffer.Start, buffer.End);

                    if (result.IsCompleted)
                        break;
                }
            } catch (OperationCanceledException) {
            } finally {
                await Reader.CompleteAsync();
            }
        }

        private static bool TryReadLine(ref ReadOnlySequence<byte> Buffer, out ReadOnlySequence<byte> Line)
        {
            SequencePosition? newline = Buffer.PositionOf((byte)'\n');
            if (newline == null) {
                Line = default;
                return false;
            }

            Line = Buffer.Slice(0, newline.Value);
            Buffer = Buffer.Slice(Buffer.GetPosition(1, newline.Value));
            return true;
        }

        private static string DecodeLine(ReadOnlySequence<byte> Line)
        {
            var text = Line.IsSingleSegment ? Encoding.UTF8.GetString(Line.FirstSpan) : Encoding.UTF8.GetString(Line.ToArray());
            return text.TrimEnd('\r');
        }

        private static Task WriteLineAsync(IServerStreamWriter<LogEntry> Res, string Message, CancellationToken Ct)
        {
            return Res.WriteAsync(new LogEntry {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Severity = LogSeverity.Unspecified,
                Message = Message
            }, Ct);
        }

        private sealed class FileChangeSignal : IDisposable
        {
            private readonly FileSystemWatcher Watcher;
            private readonly SemaphoreSlim Signal = new(0, 1);

            public FileChangeSignal(string FilePath)
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (String.IsNullOrEmpty(dir))
                    dir = ".";

                Watcher = new FileSystemWatcher(dir, Path.GetFileName(FilePath)) {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    InternalBufferSize = 64 * 1024
                };

                Watcher.Changed += (_, _) => Release();
                Watcher.Created += (_, _) => Release();
                Watcher.Renamed += (_, _) => Release();
                Watcher.Deleted += (_, _) => Release();
                Watcher.Error += (_, _) => Release();

                Watcher.EnableRaisingEvents = true;
            }

            private void Release()
            {
                if (Signal.CurrentCount == 0) {
                    try {
                        Signal.Release();
                    } catch (SemaphoreFullException) {
                    }
                }
            }

            public Task WaitAsync(CancellationToken CancellationToken) => Signal.WaitAsync(CancellationToken);

            public void Dispose()
            {
                Watcher.EnableRaisingEvents = false;
                Watcher.Dispose();
                Signal.Dispose();
            }
        }

        private static async Task<IEnumerable<string>> ReadLastLines(FileStream Stream, int Count, CancellationToken CancellationToken)
        {
            if (Stream.Length == 0)
                return [];

            var start = FindLineOffset(Stream, Count);
            Stream.Seek(start, SeekOrigin.Begin);

            var reader = PipeReader.Create(Stream, new StreamPipeReaderOptions(bufferSize: BUFFER_SIZE, leaveOpen: true));
            var lines = new List<string>();

            try {
                while (true) {
                    ReadResult result = await reader.ReadAsync(CancellationToken);
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                        lines.Add(DecodeLine(line));

                    if (result.IsCompleted) {
                        if (!buffer.IsEmpty) // final line with no terminating newline
                            lines.Add(DecodeLine(buffer));
                        reader.AdvanceTo(buffer.End);
                        break;
                    }

                    reader.AdvanceTo(buffer.Start, buffer.End);
                }
            } finally {
                await reader.CompleteAsync();
            }

            // `start` lands on a boundary, so we may have gathered `Count` or `Count`+1 lines
            return lines.Count > Count ? lines.GetRange(lines.Count - Count, Count) : lines;
        }

        private static long FindLineOffset(FileStream Stream, int LineFromEnd)
        {
            long windowStart = Stream.Length;
            int nlCount = 0;
            var buf = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
            try {
                while (windowStart > 0) {
                    var blockStart = Math.Max(0, windowStart - BUFFER_SIZE);
                    var blockLen = (int)(windowStart - blockStart);

                    Stream.Seek(blockStart, SeekOrigin.Begin);
                    blockLen = Stream.ReadAtLeast(buf.AsSpan(0, blockLen), blockLen, throwOnEndOfStream: false);

                    for (int x = blockLen - 1; x >= 0; x--)
                        if (buf[x] == (byte)'\n' && ++nlCount > LineFromEnd)
                            return blockStart + x + 1;

                    windowStart = blockStart;
                }
                return 0;
            } finally {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }
    }
}
