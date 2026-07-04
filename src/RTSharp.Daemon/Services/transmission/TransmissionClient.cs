using Microsoft.Extensions.Options;

using RTSharp.Daemon.Exceptions;
using RTSharp.Daemon.Utils;

using Transmission.Net.Core;

namespace RTSharp.Daemon.Services.transmission;

public class TransmissionClient
{
    ConfigModel Config;

    public string Uri => Config.Uri;

    public TransmissionClient(IOptionsFactory<ConfigModel> Opts, [ServiceKey] string InstanceKey)
    {
        Config = Opts.Create(InstanceKey);
    }

    private Transmission.Net.TransmissionClient? Client;

    public async ValueTask<ITransmissionClient> Init()
    {
        if (Client == null) {
            try {
                Client = new Transmission.Net.TransmissionClient(Config.Uri, null, Config.Username, Config.Password);

                var session = await Client.GetSessionInformationAsync();

                Config.ConfigDir ??= session!.ConfigDirectory!;

                return Client;
            } catch (Exception ex) {
                Client = null;

                if (ex.IsConnectionFailure())
                    throw new DataProviderFailure(ex.Message, ex, Config.Uri);

                throw;
            }
        }

        return Client;
    }

    public string ConfigDir => Config?.ConfigDir!;
}
