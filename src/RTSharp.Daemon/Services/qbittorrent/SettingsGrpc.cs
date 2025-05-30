using Google.Protobuf.WellKnownTypes;

namespace RTSharp.Daemon.Services.qbittorrent;

public class SettingsGrpc
{
    QbitClient Client;
    SessionsService Sessions;
    ILogger Logger;
    IServiceProvider ServiceProvider;
    string InstanceKey;

    public SettingsGrpc([ServiceKey] string InstanceKey, SessionsService Sessions, ILogger<Grpc> Logger, IServiceProvider ServiceProvider)
    {
        Client = ServiceProvider.GetRequiredKeyedService<QbitClient>(InstanceKey);
        this.InstanceKey = InstanceKey;
        this.ServiceProvider = ServiceProvider;
        this.Sessions = Sessions;
        this.Logger = Logger;
    }

    public async Task<StringValue> GetDefaultSavePath()
    {
        await Client.Init();

        return new StringValue { Value = await Client.Client.GetDefaultSavePathAsync() };
    }
}
