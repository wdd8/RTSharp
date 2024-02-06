#nullable enable

using RTSharp.Shared.Abstractions;

namespace RTSharp.DataProvider.Rtorrent.Plugin
{
    public class DataProviderStats : IDataProviderStats
    {
        private Plugin ThisPlugin { get; }
        public IPluginHost PluginHost { get; }

        public DataProviderStatsCapabilities Capabilities { get; } = new(
            GetStateHistory: true
        );

        public DataProviderStats(Plugin ThisPlugin)
        {
            this.ThisPlugin = ThisPlugin;
            this.PluginHost = ThisPlugin.Host;
        }
    }
}
