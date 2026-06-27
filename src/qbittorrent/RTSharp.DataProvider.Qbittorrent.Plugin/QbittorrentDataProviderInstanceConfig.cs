using RTSharp.Shared.Abstractions;

namespace RTSharp.DataProvider.Qbittorrent.Plugin;

public class QbittorrentDataProviderInstanceConfig : PluginConfigContainer<DataProviderInstanceConfig>
{
    public TimeSpan ListUpdateInterval { get; set; } = TimeSpan.FromSeconds(1);
}
