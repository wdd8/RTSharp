global using static RTSharp.DataProvider.Qbittorrent.Plugin.GlobalClient;

using Microsoft.Extensions.Configuration;

using QBittorrent.Client;
using RTSharp.Shared.Abstractions;

namespace RTSharp.DataProvider.Qbittorrent.Plugin
{
    public static class GlobalClient
    {
        public static IQBittorrentClient2 Client;

        public static IPluginHost PluginHost { get; set; }

        public static async Task Init()
        {
            if (Client == null) {
                try {
                    var uri = new Uri(PluginHost.PluginConfig.GetValue<string>("Server:Uri")!);
                    var username = PluginHost.PluginConfig.GetValue<string>("Server:Username");
                    var password = PluginHost.PluginConfig.GetValue<string>("Server:Password");

                    Client = new QBittorrentClient(uri, ApiLevel.Auto);
                    await Client.LoginAsync(username, password);
                } catch {
                    Client = null;
                }
            }
        }
    }
}
