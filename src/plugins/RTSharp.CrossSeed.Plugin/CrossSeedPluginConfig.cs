using RTSharp.Scripts.CrossSeedSearch;
using RTSharp.Shared.Abstractions;

namespace RTSharp.CrossSeed.Plugin;

public class CrossSeedPluginConfig : PluginConfigContainer<PluginInstanceConfig>
{
    public List<CrossSeedIndexer> Indexers { get; set; } = [];

    public LinkOptions LinkOptions { get; set; } = LinkOptions.ReflinkWithHardlinkFallback;

    public int SearchDelaySeconds { get; set; } = 10;
}
