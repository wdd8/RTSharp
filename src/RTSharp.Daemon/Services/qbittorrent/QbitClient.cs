using Microsoft.Extensions.Options;
using QBittorrent.Client;

namespace RTSharp.Daemon.Services.qbittorrent;

public class QbitClient
{
    ConfigModel Config;

    public QbitClient(IOptionsFactory<ConfigModel> Opts, [ServiceKey] string InstanceKey)
    {
        Config = Opts.Create(InstanceKey);
    }

    private QBittorrentClient? Client;
    private DateTime Created = DateTime.MinValue;

    public async Task<IQBittorrentClient2> Init()
    {
        if (Client == null || DateTime.UtcNow - Created > TimeSpan.FromMinutes(30)) {
            try {
                var uri = new Uri(Config.Uri);

                Client = new QBittorrentClient(uri, ApiLevel.Auto);
                await Client.LoginAsync(Config.Username, Config.Password);

                Created = DateTime.UtcNow;
                return Client;
            } catch {
                Client = null;
                Created = DateTime.MinValue;
                throw;
            }
        }

        return Client;
    }
}
