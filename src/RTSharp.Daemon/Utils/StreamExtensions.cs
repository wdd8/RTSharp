using System.Buffers;

namespace RTSharp.Daemon.Utils;

public static class StreamExtensions
{
    public static async Task<long> CopyToAsync(this Stream source, Stream destination, Action<long> progress, CancellationToken cancellationToken = default, int bufferSize = 0x40000)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try {
            int bytesRead;
            long totalRead = 0;
            long bufferCount = 0;
            var memory = buffer.AsMemory(0, bufferSize);
            while ((bytesRead = await source.ReadAsync(memory, cancellationToken)) > 0) {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                totalRead += bytesRead;
                bufferCount++;
                if (bufferCount % 100 == 0)
                    progress(totalRead);
            }

            return totalRead;
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
