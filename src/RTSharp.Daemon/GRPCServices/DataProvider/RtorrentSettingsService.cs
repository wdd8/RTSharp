using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Protocols.DataProvider.Settings;

namespace RTSharp.Daemon.GRPCServices.DataProvider
{
    public class RtorrentSettingsService(RegisteredDataProviders RegisteredDataProviders) : GRPCRtorrentSettingsService.GRPCRtorrentSettingsServiceBase
    {
        public override async Task<RtorrentSettings> GetSettings(Empty Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            if (dp.Type != DataProviderType.rtorrent)
                throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Only applicable to rtorrent data provider"));

            var settingsService = dp.Resolve<Services.rtorrent.SettingsService>();
            var settings = await settingsService.GetSettings(Services.rtorrent.SettingsService.AllSettings);

            var ret = new RtorrentSettings();
            foreach (var (k, v) in settings) {
                var prop = ret.GetType().GetProperty(Services.rtorrent.SettingsService.AllSettings.First(x => x.RtorrentSetting == k).Property);
                if (prop.PropertyType == typeof(int))
                    prop.SetValue(ret, (int)(long)v);
                else
                    prop.SetValue(ret, v);
            }

            return ret;
        }

        public override async Task<CommandReply> SetSettings(RtorrentSettings Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            if (dp.Type != DataProviderType.rtorrent)
                throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Only applicable to rtorrent data provider"));

            var settingsService = dp.Resolve<Services.rtorrent.SettingsService>();
            var ret = await settingsService.SetSettings(Req);

            return ret;
        }
    }
}
