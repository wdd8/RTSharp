using Newtonsoft.Json.Linq;

using System.Buffers;

namespace RTSharp.Daemon.Services.rtorrent
{
    public class RtorrentVersionService
    {
        private static readonly Version MINIMUM_V16 = new(0, 16, 16);
        private static readonly SearchValues<char> VERSION_CHARS = SearchValues.Create("0123456789.");

        private readonly SCGICommunication Scgi;
        private readonly ILogger<RtorrentVersionService> Logger;

        private readonly SemaphoreSlim Lock = new(1, 1);
        private Version? Cached;

        public RtorrentVersionService(IServiceProvider ServiceProvider, [ServiceKey] string InstanceKey, ILogger<RtorrentVersionService> Logger)
        {
            this.Scgi = ServiceProvider.GetRequiredKeyedService<SCGICommunication>(InstanceKey);
            this.Logger = Logger;
        }

        public void Invalidate()
        {
            Lock.Wait();
            try {
                Cached = null;
            } finally {
                Lock.Release();
            }
        }

        public async ValueTask<Version> GetVersion(CancellationToken CancellationToken = default)
        {
            if (Cached != null)
                return Cached;

            await Lock.WaitAsync(CancellationToken);
            try {
                if (Cached != null)
                    return Cached;

                string raw;
                using (var response = await Scgi.Get("<?xml version=\"1.0\"?><methodCall><methodName>system.client_version</methodName><params></params></methodCall>", CancellationToken)) {
                    var result = response.Reader();
                    XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);
                    XMLUtils.SeekFixed(ref result, XMLUtils.SINGLE_PARAM);
                    raw = XMLUtils.Decode(XMLUtils.GetValue<string>(ref result));
                }

                var span = raw.AsSpan().Trim();

                if (span.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    span = span[1..];

                var end = span.IndexOfAnyExcept(VERSION_CHARS);
                if (end != -1)
                    span = span[..end];

                span = span.TrimEnd('.');

                if (!span.IsEmpty && Version.TryParse(span, out var parsed))
                    Cached = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
                else
                    Cached = new Version(0, 0);

                Logger.LogDebug($"rtorrent v{Cached} detected");
                return Cached;
            } finally {
                Lock.Release();
            }
        }

        public async ValueTask<bool> IsV16(CancellationToken CancellationToken = default) =>
            await GetVersion(CancellationToken) >= MINIMUM_V16;
    }
}
