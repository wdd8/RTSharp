using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

namespace RTSharp.DataProvider.Transmission.Plugin
{
    public class DataProviderFiles : IDataProviderFiles
    {
        private Plugin ThisPlugin { get; }
        public Action Init { get; }
        public IPluginHost PluginHost { get; }

        public DataProviderFilesCapabilities Capabilities { get; } = new(
            GetDefaultSavePath: true
        );

        public DataProviderFiles(Plugin ThisPlugin, Action Init)
        {
            this.ThisPlugin = ThisPlugin;
            this.Init = Init;
            this.PluginHost = ThisPlugin.Host;
        }

        public async Task<string> GetDefaultSavePath()
        {
            Init();

            var info = await Client.GetSessionInformationAsync();
            return info!.DownloadDirectory!;
        }
    }
}
