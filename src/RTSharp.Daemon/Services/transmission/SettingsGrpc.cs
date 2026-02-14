using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Protocols.DataProvider.Settings;

namespace RTSharp.Daemon.Services.transmission;

public class SettingsGrpc
{
    TransmissionClient Client;
    SessionsService Sessions;
    ILogger Logger;
    IServiceProvider ServiceProvider;
    string InstanceKey;

    public SettingsGrpc([ServiceKey] string InstanceKey, SessionsService Sessions, ILogger<Grpc> Logger, IServiceProvider ServiceProvider)
    {
        Client = ServiceProvider.GetRequiredKeyedService<TransmissionClient>(InstanceKey);
        this.InstanceKey = InstanceKey;
        this.ServiceProvider = ServiceProvider;
        this.Sessions = Sessions;
        this.Logger = Logger;
    }

    public async Task<TransmissionSessionInformation> GetSessionInformation()
    {
        await Client.Init();

        var info = (await Client.Client.GetSessionInformationAsync())!;

        return new TransmissionSessionInformation
        {
            AlternativeSpeedDown = info.AlternativeSpeedDown,
            AlternativeSpeedEnabled = info.AlternativeSpeedEnabled ?? false,
            AlternativeSpeedTimeBegin = info.AlternativeSpeedTimeBegin,
            AlternativeSpeedTimeEnabled = info.AlternativeSpeedTimeEnabled ?? false,
            AlternativeSpeedTimeEnd = info.AlternativeSpeedTimeEnd,
            AlternativeSpeedTimeDay = info.AlternativeSpeedTimeDay,
            AlternativeSpeedUp = info.AlternativeSpeedUp,
            BlocklistURL = info.BlocklistURL,
            BlocklistEnabled = info.BlocklistEnabled ?? false,
            CacheSizeMB = info.CacheSizeMB,
            DownloadDirectory = info.DownloadDirectory,
            DownloadQueueSize = info.DownloadQueueSize,
            DownloadQueueEnabled = info.DownloadQueueEnabled ?? false,
            DHTEnabled = info.DHTEnabled ?? false,
            Encryption = info.Encryption switch {
                "required" => TransmissionEncryption.Required,
                "preferred" => TransmissionEncryption.Preferred,
                "tolerated" => TransmissionEncryption.Tolerated,
                _ => throw new NotSupportedException()
            },
            IdleSeedingLimit = info.IdleSeedingLimit,
            IdleSeedingLimitEnabled = info.IdleSeedingLimitEnabled ?? false,
            IncompleteDirectory = info.IncompleteDirectory,
            IncompleteDirectoryEnabled = info.IncompleteDirectoryEnabled ?? false,
            LPDEnabled = info.LPDEnabled ?? false,
            PeerLimitGlobal = info.PeerLimitGlobal,
            PeerLimitPerTorrent = info.PeerLimitPerTorrent,
            PexEnabled = info.PexEnabled ?? false,
            PeerPort = info.PeerPort ?? 0,
            PeerPortRandomOnStart = info.PeerPortRandomOnStart ?? false,
            PortForwardingEnabled = info.PortForwardingEnabled ?? false,
            QueueStalledEnabled = info.QueueStalledEnabled ?? false,
            QueueStalledMinutes = info.QueueStalledMinutes,
            RenamePartialFiles = info.RenamePartialFiles ?? false,
            SessionID = info.SessionID,
            ScriptTorrentDoneFilename = info.ScriptTorrentDoneFilename,
            ScriptTorrentDoneEnabled = info.ScriptTorrentDoneEnabled ?? false,
            SeedRatioLimit = info.SeedRatioLimit,
            SeedRatioLimited = info.SeedRatioLimited ?? false,
            SeedQueueSize = info.SeedQueueSize ?? 0,
            SeedQueueEnabled = info.SeedQueueEnabled ?? false,
            SpeedLimitDown = ((int?)info.SpeedLimitDown) ?? 0,
            SpeedLimitDownEnabled = info.SpeedLimitDownEnabled ?? false,
            SpeedLimitUp = ((int?)info.SpeedLimitUp) ?? 0,
            SpeedLimitUpEnabled = info.SpeedLimitUpEnabled ?? false,
            UtpEnabled = info.UtpEnabled ?? false,
            BlocklistSize = info.BlocklistSize ?? 0,
            ConfigDirectory = info.ConfigDirectory,
            RpcVersion = info.RpcVersion ?? 0,
            RpcVersionMinimum = info.RpcVersionMinimum ?? 0,
            Version = info.Version
        };
    }

    public async Task SetSessionSettings(TransmissionSessionInformation Input)
    {
        await Client.Init();

        await Client.Client.SetSessionSettingsAsync(new Transmission.Net.Arguments.SessionSettings {
            AlternativeSpeedDown = Input.AlternativeSpeedDown,
            AlternativeSpeedEnabled = Input.AlternativeSpeedEnabled,
            AlternativeSpeedTimeBegin = Input.AlternativeSpeedTimeBegin,
            AlternativeSpeedTimeEnabled = Input.AlternativeSpeedTimeEnabled,
            AlternativeSpeedTimeEnd = Input.AlternativeSpeedTimeEnd,
            AlternativeSpeedTimeDay = Input.AlternativeSpeedTimeDay,
            AlternativeSpeedUp = Input.AlternativeSpeedUp,
            BlocklistURL = Input.BlocklistURL,
            BlocklistEnabled = Input.BlocklistEnabled,
            CacheSizeMB = Input.CacheSizeMB,
            DownloadDirectory = Input.DownloadDirectory,
            DownloadQueueSize = Input.DownloadQueueSize,
            DownloadQueueEnabled = Input.DownloadQueueEnabled,
            DHTEnabled = Input.DHTEnabled,
            Encryption = Input.Encryption switch {
                TransmissionEncryption.Required => "required",
                TransmissionEncryption.Preferred => "preferred",
                TransmissionEncryption.Tolerated => "tolerated",
                _ => throw new NotSupportedException(),
            },
            IdleSeedingLimit = Input.IdleSeedingLimit,
            IdleSeedingLimitEnabled = Input.IdleSeedingLimitEnabled,
            IncompleteDirectory = Input.IncompleteDirectory,
            IncompleteDirectoryEnabled = Input.IncompleteDirectoryEnabled,
            LPDEnabled = Input.LPDEnabled,
            PeerLimitGlobal = Input.PeerLimitGlobal,
            PeerLimitPerTorrent = Input.PeerLimitPerTorrent,
            PexEnabled = Input.PexEnabled,
            PeerPort = Input.PeerPort,
            PeerPortRandomOnStart = Input.PeerPortRandomOnStart,
            PortForwardingEnabled = Input.PortForwardingEnabled,
            QueueStalledEnabled = Input.QueueStalledEnabled,
            QueueStalledMinutes = Input.QueueStalledMinutes,
            RenamePartialFiles = Input.RenamePartialFiles,
            ScriptTorrentDoneFilename = Input.ScriptTorrentDoneFilename,
            ScriptTorrentDoneEnabled = Input.ScriptTorrentDoneEnabled,
            SeedRatioLimit = Input.SeedRatioLimit,
            SeedRatioLimited = Input.SeedRatioLimited,
            SeedQueueSize = Input.SeedQueueSize,
            SeedQueueEnabled = Input.SeedQueueEnabled,
            SpeedLimitDown = Input.SpeedLimitDown,
            SpeedLimitDownEnabled = Input.SpeedLimitDownEnabled,
            SpeedLimitUp = Input.SpeedLimitUp,
            SpeedLimitUpEnabled = Input.SpeedLimitUpEnabled,
            StartAddedTorrents = null,
            TrashOriginalTorrentFiles = null,
            Units = null,
            UtpEnabled = null,
        });
    }
}