using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Abstractions.DataProvider;

namespace RTSharp.DataProvider.Qbittorrent.Plugin;

public class DataProviderFiles : IDataProviderFiles
{
    private Plugin ThisPlugin { get; }
    public IDataProviderHost<QbittorrentDataProviderInstanceConfig> Host => ThisPlugin.Host;

    public DataProviderFilesCapabilities Capabilities { get; } = new(
        GetDefaultSavePath: true
    );

    public DataProviderFiles(Plugin ThisPlugin)
    {
        this.ThisPlugin = ThisPlugin;
    }

    public async Task<string> GetDefaultSavePath()
    {
        var client = Host.AttachedDaemonService!.GetGrpcService<GRPCQBittorrentSettingsService.GRPCQBittorrentSettingsServiceClient>();

        return (await client.GetDefaultSavePathAsync(new Empty(), headers: Host.GetBuiltInDataProviderGrpcHeaders())).Value;
    }
}
