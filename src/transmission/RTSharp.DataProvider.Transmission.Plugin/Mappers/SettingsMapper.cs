using RTSharp.Daemon.Protocols.DataProvider.Settings;

using System;
using System.Linq;
using System.Reflection;

namespace RTSharp.DataProvider.Transmission.Plugin.Mappers;

public static class SettingsMapper
{
    public static Models.Settings MapFromProto(TransmissionSessionInformation proto)
    {
        var settings = new Models.Settings();

        // Bandwidth
        settings.Bandwidth.AlternativeSpeedDown = proto.AlternativeSpeedDown;
        settings.Bandwidth.AlternativeSpeedEnabled = proto.AlternativeSpeedEnabled;
        settings.Bandwidth.AlternativeSpeedTimeBegin = proto.AlternativeSpeedTimeBegin;
        settings.Bandwidth.AlternativeSpeedTimeEnabled = proto.AlternativeSpeedTimeEnabled;
        settings.Bandwidth.AlternativeSpeedTimeEnd = proto.AlternativeSpeedTimeEnd;
        // Days: proto uses a bitmask, model uses bools for each day (1=Sunday, 2=Monday, ..., 7=Saturday)
        if (proto.AlternativeSpeedTimeDay.HasValue)
        {
            int mask = proto.AlternativeSpeedTimeDay.Value;
            settings.Bandwidth.AlternativeSpeedTimeDay1 = (mask & (1 << 0)) != 0;
            settings.Bandwidth.AlternativeSpeedTimeDay2 = (mask & (1 << 1)) != 0;
            settings.Bandwidth.AlternativeSpeedTimeDay3 = (mask & (1 << 2)) != 0;
            settings.Bandwidth.AlternativeSpeedTimeDay4 = (mask & (1 << 3)) != 0;
            settings.Bandwidth.AlternativeSpeedTimeDay5 = (mask & (1 << 4)) != 0;
            settings.Bandwidth.AlternativeSpeedTimeDay6 = (mask & (1 << 5)) != 0;
            settings.Bandwidth.AlternativeSpeedTimeDay7 = (mask & (1 << 6)) != 0;
        }
        settings.Bandwidth.AlternativeSpeedUp = proto.AlternativeSpeedUp;
        settings.Bandwidth.SpeedLimitDown = proto.SpeedLimitDown;
        settings.Bandwidth.SpeedLimitDownEnabled = proto.SpeedLimitDownEnabled;
        settings.Bandwidth.SpeedLimitUp = proto.SpeedLimitUp;
        settings.Bandwidth.SpeedLimitUpEnabled = proto.SpeedLimitUpEnabled;

        // Download
        settings.Download.CacheSizeMB = proto.CacheSizeMB;
        settings.Download.DownloadDirectory = proto.DownloadDirectory;
        settings.Download.IncompleteDirectory = proto.IncompleteDirectory;
        settings.Download.IncompleteDirectoryEnabled = proto.IncompleteDirectoryEnabled;
        settings.Download.RenamePartialFiles = proto.RenamePartialFiles;
        settings.Download.SeedRatioLimit = proto.SeedRatioLimit;
        settings.Download.SeedRatioLimited = proto.SeedRatioLimited;
        settings.Download.IdleSeedingLimit = proto.IdleSeedingLimit;
        settings.Download.IdleSeedingLimitEnabled = proto.IdleSeedingLimitEnabled;

        // Network
        settings.Network.BlocklistURL = proto.BlocklistURL;
        settings.Network.BlocklistEnabled = proto.BlocklistEnabled;
        settings.Network.DHTEnabled = proto.DHTEnabled;
        settings.Network.LPDEnabled = proto.LPDEnabled;
        settings.Network.PexEnabled = proto.PexEnabled;
        settings.Network.UtpEnabled = proto.UtpEnabled;
        settings.Network.PeerLimitGlobal = proto.PeerLimitGlobal;
        settings.Network.PeerLimitPerTorrent = proto.PeerLimitPerTorrent;
        settings.Network.PeerPort = proto.PeerPort;
        settings.Network.PeerPortRandomOnStart = proto.PeerPortRandomOnStart;
        settings.Network.PortForwardingEnabled = proto.PortForwardingEnabled;
        settings.Network.Encryption = proto.Encryption switch
        {
            TransmissionEncryption.Required => "Required",
            TransmissionEncryption.Preferred => "Preferred",
            TransmissionEncryption.Tolerated => "Tolerated",
            _ => null
        };

        // Queue
        settings.Queue.QueueStalledEnabled = proto.QueueStalledEnabled;
        settings.Queue.QueueStalledMinutes = proto.QueueStalledMinutes;
        settings.Queue.SeedQueueSize = proto.SeedQueueSize;
        settings.Queue.SeedQueueEnabled = proto.SeedQueueEnabled;
        settings.Queue.DownloadQueueSize = proto.DownloadQueueSize;
        settings.Queue.DownloadQueueEnabled = proto.DownloadQueueEnabled;

        return settings;
    }

    public static TransmissionSessionInformation MapToProto(Models.Settings model)
    {
        var proto = new TransmissionSessionInformation();

        // Bandwidth
        proto.AlternativeSpeedDown = model.Bandwidth.AlternativeSpeedDown;
        proto.AlternativeSpeedEnabled = model.Bandwidth.AlternativeSpeedEnabled;
        proto.AlternativeSpeedTimeBegin = model.Bandwidth.AlternativeSpeedTimeBegin;
        proto.AlternativeSpeedTimeEnabled = model.Bandwidth.AlternativeSpeedTimeEnabled;
        proto.AlternativeSpeedTimeEnd = model.Bandwidth.AlternativeSpeedTimeEnd;
        // Days: model uses bools, proto uses a bitmask
        int dayMask = 0;
        if (model.Bandwidth.AlternativeSpeedTimeDay1) dayMask |= (1 << 0);
        if (model.Bandwidth.AlternativeSpeedTimeDay2) dayMask |= (1 << 1);
        if (model.Bandwidth.AlternativeSpeedTimeDay3) dayMask |= (1 << 2);
        if (model.Bandwidth.AlternativeSpeedTimeDay4) dayMask |= (1 << 3);
        if (model.Bandwidth.AlternativeSpeedTimeDay5) dayMask |= (1 << 4);
        if (model.Bandwidth.AlternativeSpeedTimeDay6) dayMask |= (1 << 5);
        if (model.Bandwidth.AlternativeSpeedTimeDay7) dayMask |= (1 << 6);
        proto.AlternativeSpeedTimeDay = dayMask;
        proto.AlternativeSpeedUp = model.Bandwidth.AlternativeSpeedUp;
        proto.SpeedLimitDown = model.Bandwidth.SpeedLimitDown;
        proto.SpeedLimitDownEnabled = model.Bandwidth.SpeedLimitDownEnabled;
        proto.SpeedLimitUp = model.Bandwidth.SpeedLimitUp;
        proto.SpeedLimitUpEnabled = model.Bandwidth.SpeedLimitUpEnabled;

        // Download
        proto.CacheSizeMB = model.Download.CacheSizeMB;
        proto.DownloadDirectory = model.Download.DownloadDirectory;
        proto.IncompleteDirectory = model.Download.IncompleteDirectory;
        proto.IncompleteDirectoryEnabled = model.Download.IncompleteDirectoryEnabled;
        proto.RenamePartialFiles = model.Download.RenamePartialFiles;
        proto.SeedRatioLimit = model.Download.SeedRatioLimit;
        proto.SeedRatioLimited = model.Download.SeedRatioLimited;
        proto.IdleSeedingLimit = model.Download.IdleSeedingLimit;
        proto.IdleSeedingLimitEnabled = model.Download.IdleSeedingLimitEnabled;

        // Network
        proto.BlocklistURL = model.Network.BlocklistURL;
        proto.BlocklistEnabled = model.Network.BlocklistEnabled;
        proto.DHTEnabled = model.Network.DHTEnabled;
        proto.LPDEnabled = model.Network.LPDEnabled;
        proto.PexEnabled = model.Network.PexEnabled;
        proto.UtpEnabled = model.Network.UtpEnabled;
        proto.PeerLimitGlobal = model.Network.PeerLimitGlobal;
        proto.PeerLimitPerTorrent = model.Network.PeerLimitPerTorrent;
        proto.PeerPort = model.Network.PeerPort;
        proto.PeerPortRandomOnStart = model.Network.PeerPortRandomOnStart;
        proto.PortForwardingEnabled = model.Network.PortForwardingEnabled;
        proto.Encryption = model.Network.Encryption switch
        {
            "Required" => TransmissionEncryption.Required,
            "Preferred" => TransmissionEncryption.Preferred,
            "Tolerated" => TransmissionEncryption.Tolerated,
            _ => TransmissionEncryption.Max
        };

        // Queue
        proto.QueueStalledEnabled = model.Queue.QueueStalledEnabled;
        proto.QueueStalledMinutes = model.Queue.QueueStalledMinutes;
        proto.SeedQueueSize = model.Queue.SeedQueueSize;
        proto.SeedQueueEnabled = model.Queue.SeedQueueEnabled;
        proto.DownloadQueueSize = model.Queue.DownloadQueueSize;
        proto.DownloadQueueEnabled = model.Queue.DownloadQueueEnabled;

        return proto;
    }
}