using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Protocols.DataProvider.Settings;

namespace RTSharp.Daemon.GRPCServices.DataProvider
{
    public class RtorrentSettingsService : GRPCRtorrentSettingsService.GRPCRtorrentSettingsServiceBase
    {
        public RtorrentSettingsService(Services.rtorrent.SettingsService Settings)
        {
            this.Settings = Settings;
        }

        public Services.rtorrent.SettingsService Settings { get; }

        public override async Task<RtorrentSettings> GetSettings(Empty Req, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            if (dp != DataProviderName.rtorrent)
                throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Only applicable to rtorrent data provider"));

            var settings = await Settings.GetSettings(Services.rtorrent.SettingsService.AllSettings);

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
            var dp = Utils.GetDataProviderName(Ctx);
            if (dp != DataProviderName.rtorrent)
                throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Only applicable to rtorrent data provider"));

            var ret = await Settings.SetSettings(Req);

            return ret;
        }
    }
}
