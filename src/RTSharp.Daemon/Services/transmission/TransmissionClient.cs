using Microsoft.Extensions.Options;

using Transmission.Net.Core;

namespace RTSharp.Daemon.Services.transmission;

public class TransmissionClient
{
    ConfigModel Config;

    public TransmissionClient(IOptionsFactory<ConfigModel> Opts, [ServiceKey] string InstanceKey)
    {
        Config = Opts.Create(InstanceKey);
    }

    public ITransmissionClient Client;

    public async ValueTask Init()
    {
        if (Client == null) {
            try {
                Client = new Transmission.Net.TransmissionClient(Config.Uri, null, Config.Username, Config.Password);

                var session = await Client.GetSessionInformationAsync();

                Config.ConfigDir ??= session!.ConfigDirectory!;
            } catch {
                Client = null;
                throw;
            }
        }
    }

    public string ConfigDir => Config?.ConfigDir!;
}
