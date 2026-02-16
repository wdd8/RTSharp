using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.DataProvider.Transmission.Plugin;

public class DataProviderFiles : IDataProviderFiles
{
    private Plugin ThisPlugin { get; }
    public IPluginHost PluginHost { get; }

    public DataProviderFilesCapabilities Capabilities { get; } = new(
        GetDefaultSavePath: true
    );

    public DataProviderFiles(Plugin ThisPlugin)
    {
        this.ThisPlugin = ThisPlugin;
        this.PluginHost = ThisPlugin.Host;
    }

    public async Task<string> GetDefaultSavePath()
    {
        var client = PluginHost.AttachedDaemonService.GetGrpcService<GRPCTransmissionSettingsService.GRPCTransmissionSettingsServiceClient>();

        return (await client.GetSessionInformationAsync(new Empty(), headers: ThisPlugin.DataProvider.GetBuiltInDataProviderGrpcHeaders())).DownloadDirectory;
    }
}
