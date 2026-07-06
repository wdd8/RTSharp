using System.Diagnostics;

namespace RTSharp.Daemon.Services.rtorrent
{
    public class RtorrentMonitor : BackgroundService
    {
        private readonly IServiceScopeFactory ScopeFactory;
        private readonly ILogger<RtorrentMonitor> Logger;
        private readonly string InstanceKey;

        public RtorrentMonitor(IServiceScopeFactory ScopeFactory, [ServiceKey] string InstanceKey, ILogger<RtorrentMonitor> Logger)
        {
            this.InstanceKey = InstanceKey;
            this.ScopeFactory = ScopeFactory;
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
                    await proc.WaitForExitAsync(stoppingToken);
                } catch (TaskCanceledException) { }
            }
        }
    }
}
