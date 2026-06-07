using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using RTSharp.Daemon.Protocols.DataProvider.Settings;

namespace RTSharp.Daemon.GRPCServices.DataProvider
{
    public class QBittorrentSettingsService(RegisteredDataProviders RegisteredDataProviders) : GRPCQBittorrentSettingsService.GRPCQBittorrentSettingsServiceBase
    {
        public override async Task<StringValue> GetDefaultSavePath(Empty Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            if (dp.Type != DataProviderType.qbittorrent)
                throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Only applicable to qbittorrent data provider"));

            var settings = dp.Resolve<Services.qbittorrent.SettingsGrpc>();

            return await settings.GetDefaultSavePath();
        }

        public override async Task<QBittorrentSettings> GetSettings(Empty Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            if (dp.Type != DataProviderType.qbittorrent)
                throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Only applicable to qbittorrent data provider"));

            var settings = dp.Resolve<Services.qbittorrent.SettingsGrpc>();

            return await settings.GetSettings();
        }

        public override async Task<Empty> SetSettings(QBittorrentSettings Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            if (dp.Type != DataProviderType.qbittorrent)
                throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Only applicable to qbittorrent data provider"));

            var settings = dp.Resolve<Services.qbittorrent.SettingsGrpc>();

            await settings.SetSettings(Req);
            return new Empty();
        }

        public override async Task<QBittorrentNetworkInterfaces> GetNetworkInterfaces(Empty Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            if (dp.Type != DataProviderType.qbittorrent)
                throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Only applicable to qbittorrent data provider"));

            var settings = dp.Resolve<Services.qbittorrent.SettingsGrpc>();
            return await settings.GetNetworkInterfaces();
        }

        public override async Task<QBittorrentNetworkInterfaceAddresses> GetNetworkInterfaceAddresses(QBittorrentNetworkInterfaceAddressRequest Req, ServerCallContext Ctx)
        {
            var dp = RegisteredDataProviders.GetDataProvider(Ctx);
            if (dp.Type != DataProviderType.qbittorrent)
                throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Only applicable to qbittorrent data provider"));

            var settings = dp.Resolve<Services.qbittorrent.SettingsGrpc>();
            return await settings.GetNetworkInterfaceAddresses(Req.InterfaceId);
        }
    }
}
