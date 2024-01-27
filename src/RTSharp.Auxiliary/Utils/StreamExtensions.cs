namespace RTSharp.Auxiliary.Utils;

public static class StreamExtensions
{
    public static async Task<long> CopyToAsync(this Stream source, Stream destination, IProgress<long> progress, CancellationToken cancellationToken = default, int bufferSize = 0xFFFF)
    {
        var buffer = new byte[bufferSize];
        int bytesRead;
        long totalRead = 0;
        long bufferCount = 0;
        while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0) {
            await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            totalRead += bytesRead;
            bufferCount++;
            if (bufferCount % 100 == 0)
                progress.Report(totalRead);
        }

        return totalRead;
    }
}
