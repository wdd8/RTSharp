using System.Linq;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using RTSharp.DataProvider.Rtorrent.Protocols;
using RTSharp.DataProvider.Rtorrent.Protocols.Types;

namespace RTSharp.DataProvider.Rtorrent.Server.GRPCServices
{
    public class SettingsService : GRPCSettingsService.GRPCSettingsServiceBase
    {
        private ILogger<SettingsService> Logger { get; }
        private Services.SettingsService Settings { get; }

        public SettingsService(ILogger<SettingsService> Logger, Services.SettingsService Settings)
        {
            this.Logger = Logger;
            this.Settings = Settings;
		}

        

		public override async Task<Protocols.Settings> GetSettings(Empty Req, ServerCallContext Ctx)
		{
			var settings = await Settings.GetSettings(Services.SettingsService.AllSettings);

			var ret = new Protocols.Settings();
			foreach (var (k, v) in settings) {
				var prop = ret.GetType().GetProperty(Services.SettingsService.AllSettings.First(x => x.RtorrentSetting == k).Property);
				if (prop.PropertyType == typeof(int))
					prop.SetValue(ret, (int)(long)v);
				else
					prop.SetValue(ret, v);
			}

			return ret;
		}

		public override async Task<CommandReply> SetSettings(Settings Req, ServerCallContext Ctx)
		{
			var ret = await Settings.SetSettings(Req);

			return ret;
		}

		public override Task<Empty> Ping(Empty request, ServerCallContext context) => Task.FromResult(new Empty());
	}
}
