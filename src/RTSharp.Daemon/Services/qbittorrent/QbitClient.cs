using Microsoft.Extensions.Options;
using QBittorrent.Client;
using RTSharp.Daemon.Exceptions;
using RTSharp.Daemon.Utils;
using System.Threading;

namespace RTSharp.Daemon.Services.qbittorrent;

public class QbitClient
{
    ConfigModel Config;

    public string Uri => Config.Uri;

    public QbitClient(IOptionsFactory<ConfigModel> Opts, [ServiceKey] string InstanceKey)
    {
        Config = Opts.Create(InstanceKey);
    }

    private volatile QBittorrentClient? Client;
    private long CreatedTicks = DateTime.MinValue.Ticks;

    public async Task<IQBittorrentClient2> Init()
    {
        if (Client == null || (String.IsNullOrEmpty(Config.ApiKey) && DateTime.UtcNow - new DateTime(Interlocked.Read(ref CreatedTicks), DateTimeKind.Utc) > TimeSpan.FromMinutes(30))) {
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

                Interlocked.Exchange(ref CreatedTicks, DateTime.UtcNow.Ticks);
                return Client;
            } catch (Exception ex) {
                Client = null;
                Interlocked.Exchange(ref CreatedTicks, DateTime.MinValue.Ticks);

                if (ex.IsConnectionFailure())
                    throw new DataProviderFailure(ex.Message, ex, Config.Uri);

                throw;
            }
        }

        return Client;
    }
}
