using BencodeNET.Objects;
using BencodeNET.Parsing;

using RTSharp.Shared.Utils;

using System.Diagnostics;

namespace RTSharp.Daemon.Services.rtorrent
{
    public class RtorrentMonitor : BackgroundService
    {
        private readonly IServiceScopeFactory ScopeFactory;
        private readonly V1ToV2InfoHashCache Cache;
        private readonly ILogger<RtorrentMonitor> Logger;
        private readonly string InstanceKey;

        public RtorrentMonitor(IServiceScopeFactory ScopeFactory, [ServiceKey] string InstanceKey, V1ToV2InfoHashCache Cache, ILogger<RtorrentMonitor> Logger)
        {
            this.InstanceKey = InstanceKey;
            this.ScopeFactory = ScopeFactory;
            this.Cache = Cache;
            this.Logger = Logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested) {
                using var scope = ScopeFactory.CreateScope();
                var scgi = scope.ServiceProvider.GetRequiredKeyedService<SCGICommunication>(InstanceKey);

                SCGICommunication.ScgiResponse pidResponse;
                try {
                    pidResponse = await scgi.Get("<?xml version=\"1.0\"?><methodCall><methodName>system.pid</methodName><params></params></methodCall>", stoppingToken);
                } catch {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                long pid;
                using (pidResponse) {
                    var result = pidResponse.Reader();
                    XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);
                    XMLUtils.SeekFixed(ref result, XMLUtils.SINGLE_PARAM);
                    pid = XMLUtils.GetValue<long>(ref result);
                }

                Process proc;
                try {
                    proc = Process.GetProcessById((int)pid);
                } catch (Exception ex) {
                    await Task.Delay(10000, stoppingToken);
                    Logger.LogError(ex, "Failed to get process by pid provided by rtorrent");
                    continue;
                }

                Logger.LogTrace("Rtorrent: " + pid);

                using var response = await scgi.Get("<?xml version=\"1.0\" ?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data><value><struct><member><name>methodName</name><value><string>method.set_key</string></value></member><member><name>params</name><value><array><data><value><string></string></value><value><string>event.download.finished</string></value><value><string>seedingtimertsharp</string></value><value><string>d.custom.set=seedingtime,\"$execute.capture={date,+%s}\"</string></value></data></array></value></member></struct></value><value><struct><member><name>methodName</name><value><string>method.set_key</string></value></member><member><name>params</name><value><array><data><value><string></string></value><value><string>event.download.inserted_new</string></value><value><string>addtimertsharp</string></value><value><string>d.custom.set=addtime,\"$execute.capture={date,+%s}\"</string></value></data></array></value></member></struct></value><value><struct><member><name>methodName</name><value><string>network.xmlrpc.size_limit.set</string></value></member><member><name>params</name><value><array><data><value><string></string></value><value><i8>67108863</i8></value></data></array></value></member></struct></value></data></array></value></param></params></methodCall>", stoppingToken);

                var responseStr = XMLUtils.GetUtf8String(response.Body);
                if (responseStr.Contains("faultCode", StringComparison.Ordinal)) {
                    Logger.LogError($"Rtorrent triggers: {responseStr}");
                    Logger.LogError("Torrent add/finish times may be unavailable");
                } else {
                    Logger.LogInformation("Rtorrent triggers set");
                }

                try {
                    await PopulateInfoHashCache(scgi, scope, stoppingToken);
                } catch (Exception ex) {
                    Logger.LogWarning(ex, "Failed to populate infohash cache");
                }

                try {
                    await proc.WaitForExitAsync(stoppingToken);
                } catch (TaskCanceledException) { }
            }
        }

        private async Task PopulateInfoHashCache(SCGICommunication Scgi, IServiceScope Scope, CancellationToken StoppingToken)
        {
            var allV1 = new List<byte[]>();
            using (var response = await Scgi.Get("<?xml version=\"1.0\" ?><methodCall><methodName>d.multicall2</methodName><params><param><value><string></string></value></param><param><value><string>main</string></value></param><param><value><string>d.hash=</string></value></param></params></methodCall>", StoppingToken)) {
                var result = response.Reader();
                XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);
                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_START);

                while (!XMLUtils.CheckFor(result, XMLUtils.DATA_TOKEN_END)) {
                    XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
                    var hashRaw = XMLUtils.GetValue<string>(ref result);
                    XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
                    allV1.Add(Convert.FromHexString(hashRaw));
                }
            }

            var unknown = allV1.Where(v1 => !Cache.Contains(v1)).ToList();
            if (unknown.Count == 0)
                return;

            var op = Scope.ServiceProvider.GetRequiredKeyedService<RTorrentOpService>(InstanceKey);
            var paths = await op.GetDotTorrentFilePaths(unknown);

            var parser = new BencodeParser();
            int added = 0, failed = 0;
            foreach (var v1 in unknown) {
                try {
                    var bytes = await File.ReadAllBytesAsync(paths[v1], StoppingToken);
                    using var ms = new MemoryStream(bytes);
                    var dict = parser.Parse<BDictionary>(ms);
                    var (_, v2) = BencodeUtils.GetInfoHashes(dict);
                    Cache.Add(v1, v2);
                    added++;
                } catch (Exception ex) {
                    failed++;
                    Logger.LogTrace(ex, "Failed to read session .torrent for {Hash}", Convert.ToHexString(v1));
                }
            }

            Logger.LogInformation("Infohash cache populated: {Added} added, {Failed} failed, {Total} total", added, failed, allV1.Count);
        }
    }
}
