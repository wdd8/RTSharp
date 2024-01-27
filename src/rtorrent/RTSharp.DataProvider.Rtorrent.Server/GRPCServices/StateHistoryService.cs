using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using RTSharp.DataProvider.Rtorrent.Protocols;

namespace RTSharp.DataProvider.Rtorrent.Server.GRPCServices
{
	public class StateHistoryService : GRPCStateHistoryService.GRPCStateHistoryServiceBase
	{
		private ILogger<StateHistoryService> Logger { get; }
		private Services.StateHistoryService StateHistory { get; }
		private IServiceScopeFactory ScopeFactory { get; }
		private IApplicationLifetime Lifetime { get; }


		public StateHistoryService(ILogger<StateHistoryService> Logger, Services.StateHistoryService StateHistory, IServiceScopeFactory ScopeFactory, IApplicationLifetime Lifetime)
		{
			this.Logger = Logger;
			this.StateHistory = StateHistory;
			this.ScopeFactory = ScopeFactory;
			this.Lifetime = Lifetime;
		}
	}
}
