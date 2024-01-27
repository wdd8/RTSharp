using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace RTSharp.DataProvider.Rtorrent.Server.Utils
{
	public static class StreamExtensions
	{
		public static async Task CopyToAsync(this Stream source, Stream destination, IProgress<long> progress, CancellationToken cancellationToken = default(CancellationToken), int bufferSize = 0xFFFF)
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
		}
	}
}
