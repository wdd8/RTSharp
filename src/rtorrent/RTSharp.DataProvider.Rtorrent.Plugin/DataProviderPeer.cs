#nullable enable

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.DataProvider.Rtorrent.Plugin;

public class DataProviderPeer : IDataProviderPeer
{
    private Plugin ThisPlugin { get; }
    public IPluginHost PluginHost { get; }

    public DataProviderPeerCapabilities Capabilities { get; } = new(
        AddPeer: false,
        BanPeer: false,
        KickPeer: false,
        SnubPeer: false,
        UnsnubPeer: false
    );

    public DataProviderPeer(Plugin ThisPlugin)
    {
        this.ThisPlugin = ThisPlugin;
        this.PluginHost = ThisPlugin.Host;
    }
}
