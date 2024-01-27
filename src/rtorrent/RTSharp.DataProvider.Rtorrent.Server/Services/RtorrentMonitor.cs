using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RTSharp.DataProvider.Rtorrent.Server.Utils;

namespace RTSharp.DataProvider.Rtorrent.Server.Services
{
	public class RtorrentMonitor : BackgroundService
	{
		private readonly IServiceScopeFactory ScopeFactory;
		private readonly ILogger<RtorrentMonitor> Logger;

		public RtorrentMonitor(IServiceScopeFactory ScopeFactory, ILogger<RtorrentMonitor> Logger)
		{
			this.ScopeFactory = ScopeFactory;
			this.Logger = Logger;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested) {
				using var scope = ScopeFactory.CreateScope();
				var scgi = scope.ServiceProvider.GetRequiredService<SCGICommunication>();

				ReadOnlyMemory<byte> result;
				try {
					result = await scgi.Get("<?xml version=\"1.0\"?><methodCall><methodName>system.pid</methodName><params></params></methodCall>", stoppingToken);
				} catch {
					await Task.Delay(1000, stoppingToken);
					continue;
				}

				XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);
				XMLUtils.SeekFixed(ref result, XMLUtils.SINGLE_PARAM);
				var pid = XMLUtils.GetValue<long>(ref result);

				Process proc;
				try {
					proc = Process.GetProcessById((int)pid);
				} catch (Exception ex) {
					await Task.Delay(10000, stoppingToken);
					Logger.LogError(ex, "Failed to get process by pid provided by rtorrent");
					continue;
				}

				Logger.LogTrace("Rtorrent: " + pid);

				var response = await scgi.Get("<?xml version=\"1.0\" ?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data><value><struct><member><name>methodName</name><value><string>method.set_key</string></value></member><member><name>params</name><value><array><data><value><string></string></value><value><string>event.download.finished</string></value><value><string>seedingtimertsharp</string></value><value><string>d.custom.set=seedingtime,\"$execute.capture={date,+%s}\"</string></value></data></array></value></member></struct></value><value><struct><member><name>methodName</name><value><string>method.set_key</string></value></member><member><name>params</name><value><array><data><value><string></string></value><value><string>event.download.inserted_new</string></value><value><string>addtimertsharp</string></value><value><string>d.custom.set=addtime,\"$execute.capture={date,+%s}\"</string></value></data></array></value></member></struct></value><value><struct><member><name>methodName</name><value><string>network.xmlrpc.size_limit.set</string></value></member><member><name>params</name><value><array><data><value><string></string></value><value><i8>67108863</i8></value></data></array></value></member></struct></value></data></array></value></param></params></methodCall>", stoppingToken);

				if (Encoding.UTF8.GetString(response.Span).Contains("faultCode")) {
					Logger.LogError($"Rtorrent triggers: {Encoding.UTF8.GetString(response.Span)}");
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
