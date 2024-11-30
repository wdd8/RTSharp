using QBittorrent.Client;
using RTSharp.Shared.Abstractions;

namespace RTSharp.Daemon.Services.qbittorrent
{
    public class QbitClient(IConfiguration Config)
    {
        public IQBittorrentClient2 Client;

        public async Task Init()
        {
            if (Client == null) {
                try {
                    var uri = new Uri(Config.GetValue<string>("qbittorrent:Uri")!);
                    var username = Config.GetValue<string>("qbittorrent:Username");
                    var password = Config.GetValue<string>("qbittorrent:Password");

                    Client = new QBittorrentClient(uri, ApiLevel.Auto);
                    await Client.LoginAsync(username, password);
                } catch {
                    Client = null;
                }
            }
        }
    }
}
