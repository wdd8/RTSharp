using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.DataProvider.Transmission.Plugin;

public class DataProviderFiles : IDataProviderFiles
{
    private Plugin ThisPlugin { get; }

    public DataProviderFilesCapabilities Capabilities { get; } = new(
        GetDefaultSavePath: true,
        SetFilePriority: true
    );

    public DataProviderFiles(Plugin ThisPlugin)
    {
        this.ThisPlugin = ThisPlugin;
    }

    public async Task<string> GetDefaultSavePath()
    {
        var client = ThisPlugin.Host.AttachedDaemonService!.GetGrpcService<GRPCTransmissionSettingsService.GRPCTransmissionSettingsServiceClient>();

        return (await client.GetSessionInformationAsync(new Empty(), headers: ThisPlugin.Host.GetBuiltInDataProviderGrpcHeaders())).DownloadDirectory;
    }

    public async Task SetFilePriority(Torrent Torrent, IList<int> Indexes, RTSharp.Shared.Abstractions.File.PRIORITY Priority, CancellationToken cancellationToken = default)
    {
        var client = ThisPlugin.Host.AttachedDaemonService!.GetTorrentsService(ThisPlugin.Host.Instance);

        await client.SetFilePriority(Torrent.Hash, Indexes, Priority, cancellationToken);
    }
}
