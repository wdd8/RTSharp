using RTSharp.Shared.Abstractions;

namespace RTSharp.DataProvider.Rtorrent.Plugin;

public class RtorrentDataProviderInstanceConfig : PluginConfigContainer<DataProviderInstanceConfig>
{
    public TimeSpan ListUpdateInterval { get; set; } = TimeSpan.FromSeconds(1);
}
