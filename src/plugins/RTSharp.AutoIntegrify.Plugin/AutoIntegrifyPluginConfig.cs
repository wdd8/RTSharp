using RTSharp.Shared.Abstractions;

namespace RTSharp.AutoIntegrify.Plugin;

public class AutoIntegrifyPluginConfig : PluginConfigContainer<PluginInstanceConfig>
{
    public List<string> SearchPaths { get; set; } = [];

    public int RandomPiecesToCheck { get; set; } = 5;

    public int SearchRecursionLimit { get; set; } = 3;

    public LinkOptions LinkOptions { get; set; } = LinkOptions.ReflinkWithHardlinkFallback;
}
