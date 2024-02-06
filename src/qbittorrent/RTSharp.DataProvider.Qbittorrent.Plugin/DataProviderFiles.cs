using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

namespace RTSharp.DataProvider.Qbittorrent.Plugin
{
    public class DataProviderFiles : IDataProviderFiles
    {
        private Plugin ThisPlugin { get; }
        public Func<Task> Init { get; }
        public IPluginHost PluginHost { get; }

        public DataProviderFilesCapabilities Capabilities { get; } = new(
            GetDotTorrents: false,
            GetDefaultSavePath: true
        );

        public DataProviderFiles(Plugin ThisPlugin, Func<Task> Init)
        {
            this.ThisPlugin = ThisPlugin;
            this.Init = Init;
            this.PluginHost = ThisPlugin.Host;
        }

        public async Task<string> GetDefaultSavePath()
        {
            await Init();

            return await Client.GetDefaultSavePathAsync();
        }

        public Task<InfoHashDictionary<byte[]>> GetDotTorrents(IList<byte[]> In) => throw new NotImplementedException();
    }
}
