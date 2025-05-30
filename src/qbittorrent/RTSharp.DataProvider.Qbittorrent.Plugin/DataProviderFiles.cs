using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.Shared.Abstractions;

namespace RTSharp.DataProvider.Qbittorrent.Plugin
{
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
            var client = PluginHost.AttachedDaemonService.GetGrpcService<GRPCQBittorrentSettingsService.GRPCQBittorrentSettingsServiceClient>();

            return (await client.GetDefaultSavePathAsync(new Empty(), headers: ThisPlugin.DataProvider.GetBuiltInDataProviderGrpcHeaders())).Value;
        }
    }
}
