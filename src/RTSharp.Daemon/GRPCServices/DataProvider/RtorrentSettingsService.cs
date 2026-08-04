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
            var versionService = dp.Resolve<Services.rtorrent.RtorrentVersionService>();

            var isV16 = await versionService.IsV16(Ctx.CancellationToken);
            var settings = await settingsService.GetSettings(Services.rtorrent.SettingsService.GetSettingSpecs(isV16));

            var ret = new RtorrentSettings();
            foreach (var (k, v) in settings) {
                var prop = ret.GetType().GetProperty(k);

                if (prop == null)
                    continue;

                if (prop.PropertyType == typeof(int))
                    prop.SetValue(ret, (int)(long)v);
                else
                    prop.SetValue(ret, v);
            }

            ret.UnsupportedSettings.AddRange(Services.rtorrent.SettingsService.GetUnsupportedProperties(isV16));

            return ret;
        }

        public override async Task<CommandReply> SetSettings(RtorrentSettings Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            if (dp.Type != DataProviderType.rtorrent)
                throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Only applicable to rtorrent data provider"));

            var settingsService = dp.Resolve<Services.rtorrent.SettingsService>();
            var ret = await settingsService.SetSettings(Req, Ctx.CancellationToken);

            return ret;
        }
    }
}
