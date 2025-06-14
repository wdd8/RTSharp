using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using RTSharp.Daemon.Protocols.DataProvider.Settings;

namespace RTSharp.Daemon.GRPCServices.DataProvider;

public class TransmissionSettingsService(RegisteredDataProviders RegisteredDataProviders) : GRPCTransmissionSettingsService.GRPCTransmissionSettingsServiceBase
{
    public override async Task<StringValue> GetDefaultSavePath(Empty Req, ServerCallContext Ctx)
    {
        var dp = RegisteredDataProviders.GetDataProvider(Ctx);
        if (dp.Type != DataProviderType.transmission)
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Only applicable to transmission data provider"));

        var settings = dp.Resolve<Services.transmission.SettingsGrpc>();

        return await settings.GetDefaultSavePath();
    }
}
