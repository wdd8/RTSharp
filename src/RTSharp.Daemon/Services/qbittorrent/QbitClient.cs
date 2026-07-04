using Microsoft.Extensions.Options;
using QBittorrent.Client;
using RTSharp.Daemon.Exceptions;
using RTSharp.Daemon.Utils;

namespace RTSharp.Daemon.Services.qbittorrent;

public class QbitClient
{
    ConfigModel Config;

    public string Uri => Config.Uri;

    public QbitClient(IOptionsFactory<ConfigModel> Opts, [ServiceKey] string InstanceKey)
    {
        Config = Opts.Create(InstanceKey);
    }

    private QBittorrentClient? Client;
    private DateTime Created = DateTime.MinValue;

    public async Task<IQBittorrentClient2> Init()
    {
        if (Client == null || (String.IsNullOrEmpty(Config.ApiKey) && DateTime.UtcNow - Created > TimeSpan.FromMinutes(30))) {
            try {
                var uri = new Uri(Config.Uri);

                Client = new QBittorrentClient(uri, ApiLevel.Auto);

                if (!String.IsNullOrEmpty(Config.ApiKey)) {
                    Client.SetApiKey(Config.ApiKey);
                } else if (!String.IsNullOrEmpty(Config.Username)) {
                    await Client.LoginAsync(Config.Username, Config.Password);
                } else {
                    throw new InvalidOperationException("qbittorrent data provider requires either ApiKey or Username/Password to be configured");
                }

                Created = DateTime.UtcNow;
                return Client;
            } catch (Exception ex) {
                Client = null;
                Created = DateTime.MinValue;

                if (ex.IsConnectionFailure())
                    throw new DataProviderFailure(ex.Message, ex, Config.Uri);

                throw;
            }
        }

        return Client;
    }
}
