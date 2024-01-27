using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using RTSharp.DataProvider.Rtorrent.Protocols;

namespace RTSharp.DataProvider.Rtorrent.Server.GRPCServices
{
	public class TrackerService : GRPCTrackerService.GRPCTrackerServiceBase
	{
		private readonly Services.SettingsService Settings;
		private ILogger<TrackerService> Logger { get; }
		private SCGICommunication Scgi { get; }
		private IServiceScopeFactory ScopeFactory { get; }

		public TrackerService(ILogger<TrackerService> Logger, SCGICommunication Scgi, Services.SettingsService Settings, IServiceScopeFactory ScopeFactory)
		{
			this.Settings = Settings;
			this.Logger = Logger;
			this.Scgi = Scgi;
			this.ScopeFactory = ScopeFactory;
		}
	}
}
