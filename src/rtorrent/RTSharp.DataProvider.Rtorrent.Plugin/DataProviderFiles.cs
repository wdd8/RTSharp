#nullable enable

using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.DataProvider.Rtorrent.Plugin;

public class DataProviderFiles : IDataProviderFiles
{
    private Plugin ThisPlugin { get; }

    public DataProviderFilesCapabilities Capabilities { get; } = new(
        GetDefaultSavePath: true
    );

    public DataProviderFiles(Plugin ThisPlugin)
    {
        this.ThisPlugin = ThisPlugin;
    }

    public async Task<string> GetDefaultSavePath()
    {
        var client = ThisPlugin.Host.AttachedDaemonService!.GetGrpcService<GRPCRtorrentSettingsService.GRPCRtorrentSettingsServiceClient>();

        var settings = await client.GetSettingsAsync(new Empty(), headers: ThisPlugin.Host.GetBuiltInDataProviderGrpcHeaders());

        ThisPlugin.Host.Logger.Verbose($"Default save path: {settings.DefaultDirectoryForDownloads}");

        return settings.DefaultDirectoryForDownloads;
    }
}
