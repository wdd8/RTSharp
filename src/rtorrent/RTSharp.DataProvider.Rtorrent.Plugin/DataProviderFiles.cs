#nullable enable

using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.Shared.Abstractions;

namespace RTSharp.DataProvider.Rtorrent.Plugin
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
            var daemon = PluginHost.AttachedDaemonService;
            var client = daemon.GetGrpcService<GRPCRtorrentSettingsService.GRPCRtorrentSettingsServiceClient>();

            var settings = await client.GetSettingsAsync(new Empty(), headers: ThisPlugin.DataProvider.GetBuiltInDataProviderGrpcHeaders());

            PluginHost.Logger.Verbose($"Default save path: {settings.DefaultDirectoryForDownloads}");

            return settings.DefaultDirectoryForDownloads;
        }
    }
}
