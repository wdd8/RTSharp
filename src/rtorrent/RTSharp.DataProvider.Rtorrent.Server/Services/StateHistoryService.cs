using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Collections.Generic;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using System.Linq;
using RTSharp.Shared.Utils;
using Microsoft.Extensions.Configuration;

namespace RTSharp.DataProvider.Rtorrent.Server.Services
{
	public class StateHistoryService : BackgroundService
	{
		private readonly TorrentPolling TorrentPolling;
		private readonly IConfiguration Config;

		private ILogger<TorrentsService> Logger { get; }
		private IServiceScopeFactory ScopeFactory { get; }

		private string Url, Token;

		public StateHistoryService(ILogger<TorrentsService> Logger, IServiceScopeFactory ScopeFactory, TorrentPolling TorrentPolling, IConfiguration Config)
		{
			this.Logger = Logger;
			this.ScopeFactory = ScopeFactory;
			this.TorrentPolling = TorrentPolling;
			this.Config = Config;

			Url = Config.GetValue<string>("InfluxDb:Url");
			Token = Config.GetValue<string>("InfluxDb:Token");
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			if (Url == null) {
				Logger.LogInformation("InfluxDb capabilities disabled..");
				return;
			}

			using var client = new InfluxDBClient(Url, Token);
			var writeApi = client.GetWriteApi();
			var version = await client.VersionAsync();
			Logger.LogInformation($"InfluxDb: {version}");

			using (var sub = TorrentPolling.Subscribe(TimeSpan.FromMinutes(1))) {
				while (!stoppingToken.IsCancellationRequested) {
					var changes = await sub.GetChanges(true, stoppingToken);

					if (changes == null)
						return;

					var curTime = DateTime.UtcNow;

					var trackerStats = new Dictionary<string, (ulong Uploaded, ulong Downloaded)>();
					var trackers = changes.FullUpdate.Where(x => x.Trackers.Any()).GroupBy(x => UriUtils.GetDomainForTracker(new Uri(x.Trackers.First().Uri)));

					foreach (var tracker in trackers) {
						ulong uploaded = 0, downloaded = 0;
						foreach (var torrent in tracker) {
							uploaded += torrent.Uploaded;
							downloaded += torrent.Downloaded;
						}

						trackerStats[tracker.Key] = (uploaded, downloaded);
					}

					foreach (var (tracker, stats) in trackerStats) {
						var data = PointData.Measurement("tracker")
							.Tag("instance-name", Program.InstanceName)
							.Tag("tracker", tracker)
							.Field("uploaded", stats.Uploaded)
							.Field("downloaded", stats.Downloaded)
							.Timestamp(curTime, WritePrecision.Ms);

						writeApi.WritePoint(data, "rtsharp-rtorrent", "main");
					}
				}
			}
		}
	}
}
