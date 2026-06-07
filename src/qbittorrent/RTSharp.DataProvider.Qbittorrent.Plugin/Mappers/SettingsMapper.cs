using RTSharp.Daemon.Protocols.DataProvider.Settings;

namespace RTSharp.DataProvider.Qbittorrent.Plugin.Mappers;

public static class SettingsMapper
{
    public static readonly string[] BittorrentProtocolOptions = ["TCP and μTP", "TCP", "μTP"];
    public static readonly string[] EncryptionOptions = ["Allow encryption", "Require encryption", "Disable encryption"];
    public static readonly string[] ProxyTypeOptions = ["(None)", "HTTP", "SOCKS5", "HTTP (auth)", "SOCKS5 (auth)", "SOCKS4"];
    public static readonly string[] MaxRatioActionOptions = ["Stop torrent", "Remove torrent", "Remove torrent and its files", "Enable super seeding for torrent"];
    public static readonly string[] SchedulerDaysOptions = ["Every day", "Weekday", "Weekend", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
    public static readonly string[] UploadSlotsChokingAlgorithmOptions = ["Fixed slots", "Upload rate based"];
    public static readonly string[] SeedChokingAlgorithmOptions = ["Round-robin", "Fastest upload", "Anti-leech"];
    public static readonly string[] UtpTcpMixedModeOptions = ["Prefer TCP", "Peer proportional"];
    public static readonly string[] DynamicDnsServiceOptions = ["DynDNS", "NoIP"];
    public static readonly string[] LogAgeTypeOptions = ["Days", "Months", "Years"];
    public static readonly string[] ResumeDataStorageTypeOptions = ["Fastresume files (legacy)", "SQLite database (experimental)"];
    public static readonly string[] TorrentContentRemoveOptions = ["Delete files permanently", "Move to trash"];
    public static readonly string[] DiskIOReadModeOptions = ["Enable OS cache", "Disable OS cache"];
    public static readonly string[] DiskIOWriteModeOptions = ["Enable OS cache", "Disable OS cache"];
    public static readonly string[] TorrentContentLayoutOptions = ["Original", "Subfolder", "No subfolder"];
    public static readonly string[] TorrentStopConditionOptions = ["None", "Metadata received", "Files checked"];
    public static readonly string[] ScanDirectoryActionOptions = ["Monitored folder", "Default save location"];
    public static readonly string[] TMMDefaultModeOptions = ["Manual", "Automatic"];
    public static readonly string[] TMMCategoryChangeOptions = ["Switch torrent to Manual Mode", "Relocate torrent"];
    public static readonly string[] TMMPathChangeOptions = ["Switch affected torrents to Manual Mode", "Relocate affected torrents"];

    public static Models.Settings MapFromProto(QBittorrentSettings p)
    {
        var ret = new Models.Settings();

        // Behaviour
        ret.Behaviour.LogEnabled = p.LogEnabled ?? false;
        ret.Behaviour.LogPath = p.LogPath;
        ret.Behaviour.LogBackupEnabled = p.LogBackupEnabled ?? false;
        ret.Behaviour.LogMaxSize = p.LogMaxSize;
        ret.Behaviour.LogDeleteOld = p.LogDeleteOld ?? false;
        ret.Behaviour.LogAge = p.LogAge;
        ret.Behaviour.LogAgeType = (p.LogAgeType ?? 1) switch {
            0 => LogAgeTypeOptions[0],
            2 => LogAgeTypeOptions[2],
            _ => LogAgeTypeOptions[1]
        };

        // Downloads
        ret.Downloads.SavePath = p.SavePath;
        ret.Downloads.TempPathEnabled = p.TempPathEnabled ?? false;
        ret.Downloads.TempPath = p.TempPath;
        ret.Downloads.ExportDirectory = p.ExportDirectory;
        ret.Downloads.ExportDirectoryForFinished = p.ExportDirectoryForFinished;
        ret.Downloads.PreallocateAll = p.PreallocateAll ?? false;
        ret.Downloads.AppendExtensionToIncompleteFiles = p.AppendExtensionToIncompleteFiles ?? false;
        ret.Downloads.AutoTMMEnabledByDefault = (p.AutoTMMEnabledByDefault ?? false) ? TMMDefaultModeOptions[1] : TMMDefaultModeOptions[0];
        ret.Downloads.AutoTMMRetainedWhenCategoryChanges = (p.AutoTMMRetainedWhenCategoryChanges ?? false) ? TMMCategoryChangeOptions[1] : TMMCategoryChangeOptions[0];
        ret.Downloads.AutoTMMRetainedWhenDefaultSavePathChanges = (p.AutoTMMRetainedWhenDefaultSavePathChanges ?? false) ? TMMPathChangeOptions[1] : TMMPathChangeOptions[0];
        ret.Downloads.AutoTMMRetainedWhenCategorySavePathChanges = (p.AutoTMMRetainedWhenCategorySavePathChanges ?? false) ? TMMPathChangeOptions[1] : TMMPathChangeOptions[0];
        ret.Downloads.UseSubcategories = p.UseSubcategories ?? false;
        ret.Downloads.UseCategoryPathsInManualMode = p.UseCategoryPathsInManualMode ?? false;
        ret.Downloads.MailNotificationEnabled = p.MailNotificationEnabled ?? false;
        ret.Downloads.MailNotificationSender = p.MailNotificationSender;
        ret.Downloads.MailNotificationEmailAddress = p.MailNotificationEmailAddress;
        ret.Downloads.MailNotificationSmtpServer = p.MailNotificationSmtpServer;
        ret.Downloads.MailNotificationSslEnabled = p.MailNotificationSslEnabled ?? false;
        ret.Downloads.MailNotificationAuthenticationEnabled = p.MailNotificationAuthenticationEnabled ?? false;
        ret.Downloads.MailNotificationUsername = p.MailNotificationUsername;
        ret.Downloads.MailNotificationPassword = p.MailNotificationPassword;
        ret.Downloads.UseUnwantedFolder = p.UseUnwantedFolder ?? false;
        ret.Downloads.ScanDirectories.Clear();
        foreach (var entry in p.ScanDirectories)
            ret.Downloads.ScanDirectories.Add(new Models.ScanDirectory {
                Path = entry.Key,
                Action = entry.Value switch {
                    "0" => ScanDirectoryActionOptions[0],
                    "1" => ScanDirectoryActionOptions[1],
                    _ => entry.Value
                }
            });
        ret.Downloads.ExcludedFileNamesEnabled = p.ExcludedFileNamesEnabled ?? false;
        ret.Downloads.ExcludedFileNames = p.ExcludedFileNames;
        ret.Downloads.AutorunOnTorrentAddedEnabled = p.AutorunOnTorrentAddedEnabled ?? false;
        ret.Downloads.AutorunOnTorrentAddedProgram = p.AutorunOnTorrentAddedProgram;
        ret.Downloads.AutorunEnabled = p.AutorunEnabled ?? false;
        ret.Downloads.AutorunProgram = p.AutorunProgram;
        ret.Downloads.TorrentContentLayout = p.TorrentContentLayout switch {
            "Subfolder" => TorrentContentLayoutOptions[1],
            "NoSubfolder" => TorrentContentLayoutOptions[2],
            _ => TorrentContentLayoutOptions[0]
        };
        ret.Downloads.AddToTopOfQueue = p.AddToTopOfQueue ?? false;
        ret.Downloads.AddTorrentPaused = p.AddTorrentPaused ?? false;
        ret.Downloads.TorrentStopCondition = p.TorrentStopCondition switch {
            "MetadataReceived" => TorrentStopConditionOptions[1],
            "FilesChecked" => TorrentStopConditionOptions[2],
            _ => TorrentStopConditionOptions[0]
        };
        ret.Downloads.MergeTrackers = p.MergeTrackers ?? false;
        ret.Downloads.AutoDeleteEnabled = p.AutoDeleteMode is > 0;
        ret.Downloads.AutoDeleteAlsoWhenCancelled = p.AutoDeleteMode == 2;

        // Connection
        ret.Connection.BittorrentProtocol = p.BittorrentProtocol switch {
            QBittorrentBittorrentProtocol.Tcp => BittorrentProtocolOptions[1],
            QBittorrentBittorrentProtocol.Utp => BittorrentProtocolOptions[2],
            _ => BittorrentProtocolOptions[0]
        };
        ret.Connection.ListenPort = p.ListenPort;
        ret.Connection.UpnpEnabled = p.UpnpEnabled ?? false;
        ret.Connection.MaxConnectionsEnabled = p.MaxConnections is > 0;
        ret.Connection.MaxConnections = p.MaxConnections is > 0 ? p.MaxConnections : 500;
        ret.Connection.MaxConnectionsPerTorrentEnabled = p.MaxConnectionsPerTorrent is > 0;
        ret.Connection.MaxConnectionsPerTorrent = p.MaxConnectionsPerTorrent is > 0 ? p.MaxConnectionsPerTorrent : 100;
        ret.Connection.MaxUploadsEnabled = p.MaxUploads is > 0;
        ret.Connection.MaxUploads = p.MaxUploads is > 0 ? p.MaxUploads : 500;
        ret.Connection.MaxUploadsPerTorrentEnabled = p.MaxUploadsPerTorrent is > 0;
        ret.Connection.MaxUploadsPerTorrent = p.MaxUploadsPerTorrent is > 0 ? p.MaxUploadsPerTorrent : 100;
        ret.Connection.ProxyType = p.ProxyType switch {
            QBittorrentProxyType.Http => ProxyTypeOptions[1],
            QBittorrentProxyType.Socks5 => ProxyTypeOptions[2],
            QBittorrentProxyType.HttpAuth => ProxyTypeOptions[3],
            QBittorrentProxyType.Socks5Auth => ProxyTypeOptions[4],
            QBittorrentProxyType.Socks4 => ProxyTypeOptions[5],
            _ => ProxyTypeOptions[0]
        };
        ret.Connection.ProxyAddress = p.ProxyAddress;
        ret.Connection.ProxyPort = p.ProxyPort;
        ret.Connection.ProxyHostnameLookup = p.ProxyHostnameLookup ?? false;
        ret.Connection.ProxyAuthenticationEnabled = p.ProxyAuthenticationEnabled ?? false;
        ret.Connection.ProxyUsername = p.ProxyUsername;
        ret.Connection.ProxyPassword = p.ProxyPassword;
        ret.Connection.ProxyBittorrent = p.ProxyBittorrent ?? false;
        ret.Connection.ProxyPeerConnections = p.ProxyPeerConnections ?? false;
        ret.Connection.ProxyRss = p.ProxyRss ?? false;
        ret.Connection.ProxyMisc = p.ProxyMisc ?? false;
        ret.Connection.IpFilterEnabled = p.IpFilterEnabled ?? false;
        ret.Connection.IpFilterPath = p.IpFilterPath;
        ret.Connection.IpFilterTrackers = p.IpFilterTrackers ?? false;
        ret.Connection.BannedIpAddresses = string.Join("\n", p.BannedIpAddresses);

        // Speed
        ret.Speed.DownloadLimit = p.DownloadLimit is > 0 ? p.DownloadLimit / 1024 : p.DownloadLimit;
        ret.Speed.UploadLimit = p.UploadLimit is > 0 ? p.UploadLimit / 1024 : p.UploadLimit;
        ret.Speed.AlternativeDownloadLimit = p.AlternativeDownloadLimit is > 0 ? p.AlternativeDownloadLimit / 1024 : p.AlternativeDownloadLimit;
        ret.Speed.AlternativeUploadLimit = p.AlternativeUploadLimit is > 0 ? p.AlternativeUploadLimit / 1024 : p.AlternativeUploadLimit;
        ret.Speed.SchedulerEnabled = p.SchedulerEnabled ?? false;
        ret.Speed.ScheduleFromHour = p.ScheduleFromHour;
        ret.Speed.ScheduleFromMinute = p.ScheduleFromMinute;
        ret.Speed.ScheduleToHour = p.ScheduleToHour;
        ret.Speed.ScheduleToMinute = p.ScheduleToMinute;
        ret.Speed.SchedulerDays = p.SchedulerDays switch {
            QBittorrentSchedulerDay.Weekday => SchedulerDaysOptions[1],
            QBittorrentSchedulerDay.Weekend => SchedulerDaysOptions[2],
            QBittorrentSchedulerDay.Monday => SchedulerDaysOptions[3],
            QBittorrentSchedulerDay.Tuesday => SchedulerDaysOptions[4],
            QBittorrentSchedulerDay.Wednesday => SchedulerDaysOptions[5],
            QBittorrentSchedulerDay.Thursday => SchedulerDaysOptions[6],
            QBittorrentSchedulerDay.Friday => SchedulerDaysOptions[7],
            QBittorrentSchedulerDay.Saturday => SchedulerDaysOptions[8],
            QBittorrentSchedulerDay.Sunday => SchedulerDaysOptions[9],
            _ => SchedulerDaysOptions[0]
        };
        ret.Speed.LimitUTPRate = p.LimitUTPRate ?? false;
        ret.Speed.LimitTcpOverhead = p.LimitTcpOverhead ?? false;
        ret.Speed.LimitLAN = p.LimitLAN ?? false;

        // BitTorrent
        ret.BitTorrent.DHT = p.DHT ?? false;
        ret.BitTorrent.PeerExchange = p.PeerExchange ?? false;
        ret.BitTorrent.LocalPeerDiscovery = p.LocalPeerDiscovery ?? false;
        ret.BitTorrent.Encryption = p.Encryption switch {
            QBittorrentEncryption.ForceOn => EncryptionOptions[1],
            QBittorrentEncryption.ForceOff => EncryptionOptions[2],
            _ => EncryptionOptions[0]
        };
        ret.BitTorrent.AnonymousMode = p.AnonymousMode ?? false;
        ret.BitTorrent.QueueingEnabled = p.QueueingEnabled ?? false;
        ret.BitTorrent.MaxActiveDownloads = p.MaxActiveDownloads;
        ret.BitTorrent.MaxActiveUploads = p.MaxActiveUploads;
        ret.BitTorrent.MaxActiveTorrents = p.MaxActiveTorrents;
        ret.BitTorrent.DoNotCountSlowTorrents = p.DoNotCountSlowTorrents ?? false;
        ret.BitTorrent.SlowTorrentDownloadRateThreshold = p.SlowTorrentDownloadRateThreshold;
        ret.BitTorrent.SlowTorrentUploadRateThreshold = p.SlowTorrentUploadRateThreshold;
        ret.BitTorrent.SlowTorrentInactiveTime = p.SlowTorrentInactiveTime;
        ret.BitTorrent.MaxRatioEnabled = p.MaxRatioEnabled ?? false;
        ret.BitTorrent.MaxRatio = p.MaxRatio is > 0 ? p.MaxRatio : 1.0;
        ret.BitTorrent.MaxSeedingTimeEnabled = p.MaxSeedingTimeEnabled ?? false;
        ret.BitTorrent.MaxSeedingTime = p.MaxSeedingTime is > 0 ? p.MaxSeedingTime : 1440;
        ret.BitTorrent.MaxInactiveSeedingTimeEnabled = p.MaxInactiveSeedingTimeEnabled ?? false;
        ret.BitTorrent.MaxInactiveSeedingTime = p.MaxInactiveSeedingTime is > 0 ? p.MaxInactiveSeedingTime : 1440;
        ret.BitTorrent.MaxRatioAction = p.MaxRatioAction switch {
            QBittorrentMaxRatioAction.Remove => MaxRatioActionOptions[1],
            QBittorrentMaxRatioAction.RemoveWithContent => MaxRatioActionOptions[2],
            QBittorrentMaxRatioAction.EnableSuperSeeding => MaxRatioActionOptions[3],
            _ => MaxRatioActionOptions[0]
        };
        ret.BitTorrent.AdditionalTrackersEnabled = p.AdditionalTrackersEnabled ?? false;
        ret.BitTorrent.AdditionalTrackers = string.Join("\n", p.AdditionalTrackers);
        ret.BitTorrent.TrackerListEnabled = p.TrackerListEnabled ?? false;
        ret.BitTorrent.TrackerListUrl = p.TrackerListUrl;
        ret.BitTorrent.TrackerListContent = p.TrackerListContent;

        // RSS
        ret.Rss.RssProcessingEnabled = p.RssProcessingEnabled ?? false;
        ret.Rss.RssRefreshInterval = (int?)p.RssRefreshInterval;
        ret.Rss.RssFetchDelay = p.RssFetchDelay;
        ret.Rss.RssMaxArticlesPerFeed = p.RssMaxArticlesPerFeed;
        ret.Rss.RssAutoDownloadingEnabled = p.RssAutoDownloadingEnabled ?? false;
        ret.Rss.RssDownloadRepackProperEpisodes = p.RssDownloadRepackProperEpisodes ?? false;
        ret.Rss.RssSmartEpisodeFilters = string.Join("\n", p.RssSmartEpisodeFilters);

        // WebUI
        ret.WebUI.WebUIAddress = p.WebUIAddress;
        ret.WebUI.WebUIPort = p.WebUIPort;
        ret.WebUI.WebUIUpnp = p.WebUIUpnp ?? false;
        ret.WebUI.WebUIUsername = p.WebUIUsername;
        ret.WebUI.WebUIPasswordHash = p.WebUIPasswordHash;
        ret.WebUI.WebUIHttps = p.WebUIHttps ?? false;
        ret.WebUI.WebUISslKeyPath = p.WebUISslKeyPath;
        ret.WebUI.WebUISslCertificatePath = p.WebUISslCertificatePath;
        ret.WebUI.WebUIClickjackingProtection = p.WebUIClickjackingProtection ?? false;
        ret.WebUI.WebUICsrfProtection = p.WebUICsrfProtection ?? false;
        ret.WebUI.WebUISecureCookie = p.WebUISecureCookie ?? false;
        ret.WebUI.WebUIMaxAuthenticationFailures = p.WebUIMaxAuthenticationFailures;
        ret.WebUI.WebUIBanDuration = p.WebUIBanDuration;
        ret.WebUI.WebUISessionTimeout = p.WebUISessionTimeout;
        ret.WebUI.AlternativeWebUIEnabled = p.AlternativeWebUIEnabled ?? false;
        ret.WebUI.AlternativeWebUIPath = p.AlternativeWebUIPath;
        ret.WebUI.WebUIHostHeaderValidation = p.WebUIHostHeaderValidation ?? false;
        ret.WebUI.WebUIDomain = p.WebUIDomain;
        ret.WebUI.WebUICustomHttpHeadersEnabled = p.WebUICustomHttpHeadersEnabled ?? false;
        ret.WebUI.WebUICustomHttpHeaders = string.Join("\n", p.WebUICustomHttpHeaders);
        ret.WebUI.BypassLocalAuthentication = p.BypassLocalAuthentication ?? false;
        ret.WebUI.BypassAuthenticationSubnetWhitelistEnabled = p.BypassAuthenticationSubnetWhitelistEnabled ?? false;
        ret.WebUI.BypassAuthenticationSubnetWhitelist = string.Join("\n", p.BypassAuthenticationSubnetWhitelist);
        ret.WebUI.ReverseProxyEnabled = p.ReverseProxyEnabled ?? false;
        ret.WebUI.ReverseProxiesList = p.ReverseProxiesList;
        ret.WebUI.DynamicDnsEnabled = p.DynamicDnsEnabled ?? false;
        ret.WebUI.DynamicDnsService = p.DynamicDnsService switch {
            QBittorrentDynamicDnsService.Noip => DynamicDnsServiceOptions[1],
            _ => DynamicDnsServiceOptions[0]
        };
        ret.WebUI.DynamicDnsUsername = p.DynamicDnsUsername;
        ret.WebUI.DynamicDnsPassword = p.DynamicDnsPassword;
        ret.WebUI.DynamicDnsDomain = p.DynamicDnsDomain;

        // Advanced
        ret.Advanced.ResumeDataStorageType = p.ResumeDataStorageType == "SQLite" ? ResumeDataStorageTypeOptions[1] : ResumeDataStorageTypeOptions[0];
        ret.Advanced.TorrentContentRemoveOption = p.TorrentContentRemoveOption == "MoveToTrash" ? TorrentContentRemoveOptions[1] : TorrentContentRemoveOptions[0];
        ret.Advanced.MemoryWorkingSetLimit = p.MemoryWorkingSetLimit;
        ret.Advanced.CurrentNetworkInterface = p.CurrentNetworkInterface;
        ret.Advanced.CurrentInterfaceAddress = p.CurrentInterfaceAddress;
        ret.Advanced.SaveResumeDataInterval = p.SaveResumeDataInterval;
        ret.Advanced.SaveStatisticsInterval = p.SaveStatisticsInterval;
        ret.Advanced.TorrentFileSizeLimit = p.TorrentFileSizeLimit;
        ret.Advanced.ConfirmTorrentRecheck = p.ConfirmTorrentRecheck ?? false;
        ret.Advanced.RecheckCompletedTorrents = p.RecheckCompletedTorrents ?? false;
        ret.Advanced.ResolvePeerCountries = p.ResolvePeerCountries ?? false;
        ret.Advanced.AppInstanceName = p.AppInstanceName;
        ret.Advanced.RefreshInterval = p.RefreshInterval;
        ret.Advanced.ReannounceWhenAddressChanged = p.ReannounceWhenAddressChanged ?? false;
        ret.Advanced.LibtorrentAsynchronousIOThreads = p.LibtorrentAsynchronousIOThreads;
        ret.Advanced.LibtorrentHashingThreads = p.LibtorrentHashingThreads;
        ret.Advanced.LibtorrentFilePoolSize = p.LibtorrentFilePoolSize;
        ret.Advanced.LibtorrentOutstandingMemoryWhenCheckingTorrent = p.LibtorrentOutstandingMemoryWhenCheckingTorrent;
        ret.Advanced.LibtorrentDiskCache = p.LibtorrentDiskCache;
        ret.Advanced.LibtorrentDiskCacheExpiryInterval = p.LibtorrentDiskCacheExpiryInterval;
        ret.Advanced.LibtorrentDiskIOReadMode = p.LibtorrentDiskIOReadMode switch {
            1 => DiskIOReadModeOptions[0],  // EnableOSCache = 1
            _ => DiskIOReadModeOptions[1]   // DisableOSCache = 0
        };
        ret.Advanced.LibtorrentDiskIOWriteMode = p.LibtorrentDiskIOWriteMode switch {
            1 => DiskIOWriteModeOptions[0], // EnableOSCache = 1
            _ => DiskIOWriteModeOptions[1]  // DisableOSCache = 0
        };
        ret.Advanced.LibtorrentDiskQueueSize = p.LibtorrentDiskQueueSize;
        ret.Advanced.LibtorrentBdecodeDepthLimit = p.LibtorrentBdecodeDepthLimit;
        ret.Advanced.LibtorrentBdecodeTokenLimit = p.LibtorrentBdecodeTokenLimit;
        ret.Advanced.LibtorrentCoalesceReadsAndWrites = p.LibtorrentCoalesceReadsAndWrites ?? false;
        ret.Advanced.LibtorrentPieceExtentAffinity = p.LibtorrentPieceExtentAffinity ?? false;
        ret.Advanced.LibtorrentSendUploadPieceSuggestions = p.LibtorrentSendUploadPieceSuggestions ?? false;
        ret.Advanced.LibtorrentSendBufferWatermark = p.LibtorrentSendBufferWatermark;
        ret.Advanced.LibtorrentSendBufferLowWatermark = p.LibtorrentSendBufferLowWatermark;
        ret.Advanced.LibtorrentSendBufferWatermarkFactor = p.LibtorrentSendBufferWatermarkFactor;
        ret.Advanced.LibtorrentSocketSendBufferSize = p.LibtorrentSocketSendBufferSize;
        ret.Advanced.LibtorrentSocketReceiveBufferSize = p.LibtorrentSocketReceiveBufferSize;
        ret.Advanced.LibtorrentSocketBacklogSize = p.LibtorrentSocketBacklogSize;
        ret.Advanced.LibtorrentOutgoingPortsMin = p.LibtorrentOutgoingPortsMin;
        ret.Advanced.LibtorrentOutgoingPortsMax = p.LibtorrentOutgoingPortsMax;
        ret.Advanced.LibtorrentOutgoingConnectionsPerSecond = p.LibtorrentOutgoingConnectionsPerSecond;
        ret.Advanced.LibtorrentUpnpLeaseDuration = p.LibtorrentUpnpLeaseDuration;
        ret.Advanced.LibtorrentPeerTos = p.LibtorrentPeerTos;
        ret.Advanced.LibtorrentUtpTcpMixedModeAlgorithm = p.LibtorrentUtpTcpMixedModeAlgorithm switch {
            QBittorrentUtpTcpMixedModeAlgorithm.PeerProportional => UtpTcpMixedModeOptions[1],
            _ => UtpTcpMixedModeOptions[0]
        };
        ret.Advanced.LibtorrentAllowMultipleConnectionsFromSameIp = p.LibtorrentAllowMultipleConnectionsFromSameIp ?? false;
        ret.Advanced.LibtorrentEnableEmbeddedTracker = p.LibtorrentEnableEmbeddedTracker ?? false;
        ret.Advanced.LibtorrentEmbeddedTrackerPort = p.LibtorrentEmbeddedTrackerPort;
        ret.Advanced.EmbeddedTrackerPortForwarding = p.EmbeddedTrackerPortForwarding ?? false;
        ret.Advanced.IgnoreSslErrors = p.IgnoreSslErrors ?? false;
        ret.Advanced.PythonExecutablePath = p.PythonExecutablePath;
        ret.Advanced.LibtorrentUploadSlotsBehavior = p.LibtorrentUploadSlotsBehavior switch {
            QBittorrentChokingAlgorithm.RateBased => UploadSlotsChokingAlgorithmOptions[1],
            _ => UploadSlotsChokingAlgorithmOptions[0]
        };
        ret.Advanced.LibtorrentUploadChokingAlgorithm = p.LibtorrentUploadChokingAlgorithm switch {
            QBittorrentSeedChokingAlgorithm.FastestUpload => SeedChokingAlgorithmOptions[1],
            QBittorrentSeedChokingAlgorithm.AntiLeech => SeedChokingAlgorithmOptions[2],
            _ => SeedChokingAlgorithmOptions[0]
        };
        ret.Advanced.LibtorrentAnnounceToAllTrackers = p.LibtorrentAnnounceToAllTrackers ?? false;
        ret.Advanced.LibtorrentAnnounceToAllTiers = p.LibtorrentAnnounceToAllTiers ?? false;
        ret.Advanced.LibtorrentAnnounceIp = p.LibtorrentAnnounceIp;
        ret.Advanced.LibtorrentMaxConcurrentHttpAnnounces = p.LibtorrentMaxConcurrentHttpAnnounces;
        ret.Advanced.LibtorrentStopTrackerTimeout = p.LibtorrentStopTrackerTimeout;
        ret.Advanced.LibtorrentValidateHttpsTrackerCertificate = p.LibtorrentValidateHttpsTrackerCertificate ?? false;
        ret.Advanced.LibtorrentIdnSupportEnabled = p.LibtorrentIdnSupportEnabled ?? false;
        ret.Advanced.LibtorrentSsrfMitigation = p.LibtorrentSsrfMitigation ?? false;
        ret.Advanced.LibtorrentBlockPeersOnPrivilegedPorts = p.LibtorrentBlockPeersOnPrivilegedPorts ?? false;
        ret.Advanced.LibtorrentAnnouncePort = p.LibtorrentAnnouncePort;
        ret.Advanced.LibtorrentPeerTurnover = p.LibtorrentPeerTurnover;
        ret.Advanced.LibtorrentPeerTurnoverCutoff = p.LibtorrentPeerTurnoverCutoff;
        ret.Advanced.LibtorrentPeerTurnoverInterval = p.LibtorrentPeerTurnoverInterval;
        ret.Advanced.LibtorrentRequestQueueSize = p.LibtorrentRequestQueueSize;
        ret.Advanced.LibtorrentDhtBootstrapNodes = p.LibtorrentDhtBootstrapNodes;
        ret.Advanced.LibtorrentI2pInboundQuantity = p.LibtorrentI2PInboundQuantity;
        ret.Advanced.LibtorrentI2pOutboundQuantity = p.LibtorrentI2POutboundQuantity;
        ret.Advanced.LibtorrentI2pInboundLength = p.LibtorrentI2PInboundLength;
        ret.Advanced.LibtorrentI2pOutboundLength = p.LibtorrentI2POutboundLength;

        return ret;
    }

    public static QBittorrentSettings MapToProto(Models.Settings s)
    {
        var ret = new QBittorrentSettings {
            // Behaviour
            LogEnabled = s.Behaviour.LogEnabled,
            LogPath = s.Behaviour.LogPath,
            LogBackupEnabled = s.Behaviour.LogBackupEnabled,
            LogMaxSize = s.Behaviour.LogMaxSize,
            LogDeleteOld = s.Behaviour.LogDeleteOld,
            LogAge = s.Behaviour.LogAge,
            LogAgeType = s.Behaviour.LogAgeType switch {
                var x when x == LogAgeTypeOptions[0] => 0,
                var x when x == LogAgeTypeOptions[2] => 2,
                _ => 1
            },

            // Downloads
            SavePath = s.Downloads.SavePath,
            TempPathEnabled = s.Downloads.TempPathEnabled,
            TempPath = s.Downloads.TempPath,
            ExportDirectory = s.Downloads.ExportDirectory,
            ExportDirectoryForFinished = s.Downloads.ExportDirectoryForFinished,
            PreallocateAll = s.Downloads.PreallocateAll,
            AppendExtensionToIncompleteFiles = s.Downloads.AppendExtensionToIncompleteFiles,
            AutoTMMEnabledByDefault = s.Downloads.AutoTMMEnabledByDefault == TMMDefaultModeOptions[1],
            AutoTMMRetainedWhenCategoryChanges = s.Downloads.AutoTMMRetainedWhenCategoryChanges == TMMCategoryChangeOptions[1],
            AutoTMMRetainedWhenDefaultSavePathChanges = s.Downloads.AutoTMMRetainedWhenDefaultSavePathChanges == TMMPathChangeOptions[1],
            AutoTMMRetainedWhenCategorySavePathChanges = s.Downloads.AutoTMMRetainedWhenCategorySavePathChanges == TMMPathChangeOptions[1],
            UseSubcategories = s.Downloads.UseSubcategories,
            UseCategoryPathsInManualMode = s.Downloads.UseCategoryPathsInManualMode,
            MailNotificationEnabled = s.Downloads.MailNotificationEnabled,
            MailNotificationSender = s.Downloads.MailNotificationSender,
            MailNotificationEmailAddress = s.Downloads.MailNotificationEmailAddress,
            MailNotificationSmtpServer = s.Downloads.MailNotificationSmtpServer,
            MailNotificationSslEnabled = s.Downloads.MailNotificationSslEnabled,
            MailNotificationAuthenticationEnabled = s.Downloads.MailNotificationAuthenticationEnabled,
            MailNotificationUsername = s.Downloads.MailNotificationUsername,
            MailNotificationPassword = s.Downloads.MailNotificationPassword,
            UseUnwantedFolder = s.Downloads.UseUnwantedFolder,
            ExcludedFileNamesEnabled = s.Downloads.ExcludedFileNamesEnabled,
            ExcludedFileNames = s.Downloads.ExcludedFileNames,
            AutorunOnTorrentAddedEnabled = s.Downloads.AutorunOnTorrentAddedEnabled,
            AutorunOnTorrentAddedProgram = s.Downloads.AutorunOnTorrentAddedProgram,
            AutorunEnabled = s.Downloads.AutorunEnabled,
            AutorunProgram = s.Downloads.AutorunProgram,
            TorrentContentLayout = s.Downloads.TorrentContentLayout switch {
                var x when x == TorrentContentLayoutOptions[1] => "Subfolder",
                var x when x == TorrentContentLayoutOptions[2] => "NoSubfolder",
                _ => "Original"
            },
            AddToTopOfQueue = s.Downloads.AddToTopOfQueue,
            AddTorrentPaused = s.Downloads.AddTorrentPaused,
            TorrentStopCondition = s.Downloads.TorrentStopCondition switch {
                var x when x == TorrentStopConditionOptions[1] => "MetadataReceived",
                var x when x == TorrentStopConditionOptions[2] => "FilesChecked",
                _ => "None"
            },
            MergeTrackers = s.Downloads.MergeTrackers,
            AutoDeleteMode = s.Downloads.AutoDeleteEnabled ? (s.Downloads.AutoDeleteAlsoWhenCancelled ? 2 : 1) : 0,

            // Connection
            BittorrentProtocol = s.Connection.BittorrentProtocol switch {
                var x when x == BittorrentProtocolOptions[1] => QBittorrentBittorrentProtocol.Tcp,
                var x when x == BittorrentProtocolOptions[2] => QBittorrentBittorrentProtocol.Utp,
                _ => QBittorrentBittorrentProtocol.Both
            },
            ListenPort = s.Connection.ListenPort,
            UpnpEnabled = s.Connection.UpnpEnabled,
            MaxConnections = s.Connection.MaxConnectionsEnabled ? s.Connection.MaxConnections : -1,
            MaxConnectionsPerTorrent = s.Connection.MaxConnectionsPerTorrentEnabled ? s.Connection.MaxConnectionsPerTorrent : -1,
            MaxUploads = s.Connection.MaxUploadsEnabled ? s.Connection.MaxUploads : -1,
            MaxUploadsPerTorrent = s.Connection.MaxUploadsPerTorrentEnabled ? s.Connection.MaxUploadsPerTorrent : -1,
            ProxyType = s.Connection.ProxyType switch {
                var x when x == ProxyTypeOptions[1] => QBittorrentProxyType.Http,
                var x when x == ProxyTypeOptions[2] => QBittorrentProxyType.Socks5,
                var x when x == ProxyTypeOptions[3] => QBittorrentProxyType.HttpAuth,
                var x when x == ProxyTypeOptions[4] => QBittorrentProxyType.Socks5Auth,
                var x when x == ProxyTypeOptions[5] => QBittorrentProxyType.Socks4,
                _ => QBittorrentProxyType.None
            },
            ProxyAddress = s.Connection.ProxyAddress,
            ProxyPort = s.Connection.ProxyPort,
            ProxyHostnameLookup = s.Connection.ProxyHostnameLookup,
            ProxyAuthenticationEnabled = s.Connection.ProxyAuthenticationEnabled,
            ProxyUsername = s.Connection.ProxyUsername,
            ProxyPassword = s.Connection.ProxyPassword,
            ProxyBittorrent = s.Connection.ProxyBittorrent,
            ProxyPeerConnections = s.Connection.ProxyPeerConnections,
            ProxyRss = s.Connection.ProxyRss,
            ProxyMisc = s.Connection.ProxyMisc,
            IpFilterEnabled = s.Connection.IpFilterEnabled,
            IpFilterPath = s.Connection.IpFilterPath,
            IpFilterTrackers = s.Connection.IpFilterTrackers,

            // Speed
            DownloadLimit = s.Speed.DownloadLimit is > 0 ? s.Speed.DownloadLimit * 1024 : s.Speed.DownloadLimit,
            UploadLimit = s.Speed.UploadLimit is > 0 ? s.Speed.UploadLimit * 1024 : s.Speed.UploadLimit,
            AlternativeDownloadLimit = s.Speed.AlternativeDownloadLimit is > 0 ? s.Speed.AlternativeDownloadLimit * 1024 : s.Speed.AlternativeDownloadLimit,
            AlternativeUploadLimit = s.Speed.AlternativeUploadLimit is > 0 ? s.Speed.AlternativeUploadLimit * 1024 : s.Speed.AlternativeUploadLimit,
            SchedulerEnabled = s.Speed.SchedulerEnabled,
            ScheduleFromHour = s.Speed.ScheduleFromHour,
            ScheduleFromMinute = s.Speed.ScheduleFromMinute,
            ScheduleToHour = s.Speed.ScheduleToHour,
            ScheduleToMinute = s.Speed.ScheduleToMinute,
            SchedulerDays = s.Speed.SchedulerDays switch {
                var x when x == SchedulerDaysOptions[1] => QBittorrentSchedulerDay.Weekday,
                var x when x == SchedulerDaysOptions[2] => QBittorrentSchedulerDay.Weekend,
                var x when x == SchedulerDaysOptions[3] => QBittorrentSchedulerDay.Monday,
                var x when x == SchedulerDaysOptions[4] => QBittorrentSchedulerDay.Tuesday,
                var x when x == SchedulerDaysOptions[5] => QBittorrentSchedulerDay.Wednesday,
                var x when x == SchedulerDaysOptions[6] => QBittorrentSchedulerDay.Thursday,
                var x when x == SchedulerDaysOptions[7] => QBittorrentSchedulerDay.Friday,
                var x when x == SchedulerDaysOptions[8] => QBittorrentSchedulerDay.Saturday,
                var x when x == SchedulerDaysOptions[9] => QBittorrentSchedulerDay.Sunday,
                _ => QBittorrentSchedulerDay.Every
            },
            LimitUTPRate = s.Speed.LimitUTPRate,
            LimitTcpOverhead = s.Speed.LimitTcpOverhead,
            LimitLAN = s.Speed.LimitLAN,

            // BitTorrent
            DHT = s.BitTorrent.DHT,
            PeerExchange = s.BitTorrent.PeerExchange,
            LocalPeerDiscovery = s.BitTorrent.LocalPeerDiscovery,
            Encryption = s.BitTorrent.Encryption switch {
                var x when x == EncryptionOptions[1] => QBittorrentEncryption.ForceOn,
                var x when x == EncryptionOptions[2] => QBittorrentEncryption.ForceOff,
                _ => QBittorrentEncryption.Prefer
            },
            AnonymousMode = s.BitTorrent.AnonymousMode,
            QueueingEnabled = s.BitTorrent.QueueingEnabled,
            MaxActiveDownloads = s.BitTorrent.MaxActiveDownloads,
            MaxActiveUploads = s.BitTorrent.MaxActiveUploads,
            MaxActiveTorrents = s.BitTorrent.MaxActiveTorrents,
            DoNotCountSlowTorrents = s.BitTorrent.DoNotCountSlowTorrents,
            SlowTorrentDownloadRateThreshold = s.BitTorrent.SlowTorrentDownloadRateThreshold,
            SlowTorrentUploadRateThreshold = s.BitTorrent.SlowTorrentUploadRateThreshold,
            SlowTorrentInactiveTime = s.BitTorrent.SlowTorrentInactiveTime,
            MaxRatioEnabled = s.BitTorrent.MaxRatioEnabled,
            MaxRatio = s.BitTorrent.MaxRatio,
            MaxSeedingTimeEnabled = s.BitTorrent.MaxSeedingTimeEnabled,
            MaxSeedingTime = s.BitTorrent.MaxSeedingTime,
            MaxInactiveSeedingTimeEnabled = s.BitTorrent.MaxInactiveSeedingTimeEnabled,
            MaxInactiveSeedingTime = s.BitTorrent.MaxInactiveSeedingTime,
            MaxRatioAction = s.BitTorrent.MaxRatioAction switch {
                var x when x == MaxRatioActionOptions[1] => QBittorrentMaxRatioAction.Remove,
                var x when x == MaxRatioActionOptions[2] => QBittorrentMaxRatioAction.RemoveWithContent,
                var x when x == MaxRatioActionOptions[3] => QBittorrentMaxRatioAction.EnableSuperSeeding,
                _ => QBittorrentMaxRatioAction.Stop
            },
            AdditionalTrackersEnabled = s.BitTorrent.AdditionalTrackersEnabled,
            TrackerListEnabled = s.BitTorrent.TrackerListEnabled,
            TrackerListUrl = s.BitTorrent.TrackerListUrl,

            // RSS
            RssProcessingEnabled = s.Rss.RssProcessingEnabled,
            RssRefreshInterval = (uint?)s.Rss.RssRefreshInterval,
            RssFetchDelay = s.Rss.RssFetchDelay,
            RssMaxArticlesPerFeed = s.Rss.RssMaxArticlesPerFeed,
            RssAutoDownloadingEnabled = s.Rss.RssAutoDownloadingEnabled,
            RssDownloadRepackProperEpisodes = s.Rss.RssDownloadRepackProperEpisodes,

            // WebUI
            WebUIAddress = s.WebUI.WebUIAddress,
            WebUIPort = s.WebUI.WebUIPort,
            WebUIUpnp = s.WebUI.WebUIUpnp,
            WebUIUsername = s.WebUI.WebUIUsername,
            WebUIPassword = string.IsNullOrEmpty(s.WebUI.WebUIPassword) ? null : s.WebUI.WebUIPassword,
            WebUIHttps = s.WebUI.WebUIHttps,
            WebUISslKeyPath = s.WebUI.WebUISslKeyPath,
            WebUISslCertificatePath = s.WebUI.WebUISslCertificatePath,
            WebUIClickjackingProtection = s.WebUI.WebUIClickjackingProtection,
            WebUICsrfProtection = s.WebUI.WebUICsrfProtection,
            WebUISecureCookie = s.WebUI.WebUISecureCookie,
            WebUIMaxAuthenticationFailures = s.WebUI.WebUIMaxAuthenticationFailures,
            WebUIBanDuration = s.WebUI.WebUIBanDuration,
            WebUISessionTimeout = s.WebUI.WebUISessionTimeout,
            AlternativeWebUIEnabled = s.WebUI.AlternativeWebUIEnabled,
            AlternativeWebUIPath = s.WebUI.AlternativeWebUIPath,
            WebUIHostHeaderValidation = s.WebUI.WebUIHostHeaderValidation,
            WebUIDomain = s.WebUI.WebUIDomain,
            WebUICustomHttpHeadersEnabled = s.WebUI.WebUICustomHttpHeadersEnabled,
            BypassLocalAuthentication = s.WebUI.BypassLocalAuthentication,
            BypassAuthenticationSubnetWhitelistEnabled = s.WebUI.BypassAuthenticationSubnetWhitelistEnabled,
            ReverseProxyEnabled = s.WebUI.ReverseProxyEnabled,
            ReverseProxiesList = s.WebUI.ReverseProxiesList,
            DynamicDnsEnabled = s.WebUI.DynamicDnsEnabled,
            DynamicDnsService = s.WebUI.DynamicDnsService switch {
                var x when x == DynamicDnsServiceOptions[1] => QBittorrentDynamicDnsService.Noip,
                _ => QBittorrentDynamicDnsService.Dyndns
            },
            DynamicDnsUsername = s.WebUI.DynamicDnsUsername,
            DynamicDnsPassword = s.WebUI.DynamicDnsPassword,
            DynamicDnsDomain = s.WebUI.DynamicDnsDomain,

            // Advanced
            ResumeDataStorageType = s.Advanced.ResumeDataStorageType == ResumeDataStorageTypeOptions[1] ? "SQLite" : "Legacy",
            TorrentContentRemoveOption = s.Advanced.TorrentContentRemoveOption == TorrentContentRemoveOptions[1] ? "MoveToTrash" : "Delete",
            MemoryWorkingSetLimit = s.Advanced.MemoryWorkingSetLimit,
            CurrentNetworkInterface = s.Advanced.CurrentNetworkInterface,
            CurrentInterfaceAddress = s.Advanced.CurrentInterfaceAddress,
            SaveResumeDataInterval = s.Advanced.SaveResumeDataInterval,
            SaveStatisticsInterval = s.Advanced.SaveStatisticsInterval,
            TorrentFileSizeLimit = s.Advanced.TorrentFileSizeLimit,
            ConfirmTorrentRecheck = s.Advanced.ConfirmTorrentRecheck,
            RecheckCompletedTorrents = s.Advanced.RecheckCompletedTorrents,
            ResolvePeerCountries = s.Advanced.ResolvePeerCountries,
            AppInstanceName = s.Advanced.AppInstanceName,
            RefreshInterval = s.Advanced.RefreshInterval,
            ReannounceWhenAddressChanged = s.Advanced.ReannounceWhenAddressChanged,
            LibtorrentAsynchronousIOThreads = s.Advanced.LibtorrentAsynchronousIOThreads,
            LibtorrentHashingThreads = s.Advanced.LibtorrentHashingThreads,
            LibtorrentFilePoolSize = s.Advanced.LibtorrentFilePoolSize,
            LibtorrentOutstandingMemoryWhenCheckingTorrent = s.Advanced.LibtorrentOutstandingMemoryWhenCheckingTorrent,
            LibtorrentDiskCache = s.Advanced.LibtorrentDiskCache,
            LibtorrentDiskCacheExpiryInterval = s.Advanced.LibtorrentDiskCacheExpiryInterval,
            LibtorrentDiskIOReadMode = s.Advanced.LibtorrentDiskIOReadMode switch {
                var x when x == DiskIOReadModeOptions[0] => 1,
                _ => 0
            },
            LibtorrentDiskIOWriteMode = s.Advanced.LibtorrentDiskIOWriteMode switch {
                var x when x == DiskIOWriteModeOptions[0] => 1,
                _ => 0
            },
            LibtorrentDiskQueueSize = s.Advanced.LibtorrentDiskQueueSize,
            LibtorrentBdecodeDepthLimit = s.Advanced.LibtorrentBdecodeDepthLimit,
            LibtorrentBdecodeTokenLimit = s.Advanced.LibtorrentBdecodeTokenLimit,
            LibtorrentCoalesceReadsAndWrites = s.Advanced.LibtorrentCoalesceReadsAndWrites,
            LibtorrentPieceExtentAffinity = s.Advanced.LibtorrentPieceExtentAffinity,
            LibtorrentSendUploadPieceSuggestions = s.Advanced.LibtorrentSendUploadPieceSuggestions,
            LibtorrentSendBufferWatermark = s.Advanced.LibtorrentSendBufferWatermark,
            LibtorrentSendBufferLowWatermark = s.Advanced.LibtorrentSendBufferLowWatermark,
            LibtorrentSendBufferWatermarkFactor = s.Advanced.LibtorrentSendBufferWatermarkFactor,
            LibtorrentSocketSendBufferSize = s.Advanced.LibtorrentSocketSendBufferSize,
            LibtorrentSocketReceiveBufferSize = s.Advanced.LibtorrentSocketReceiveBufferSize,
            LibtorrentSocketBacklogSize = s.Advanced.LibtorrentSocketBacklogSize,
            LibtorrentOutgoingPortsMin = s.Advanced.LibtorrentOutgoingPortsMin,
            LibtorrentOutgoingPortsMax = s.Advanced.LibtorrentOutgoingPortsMax,
            LibtorrentOutgoingConnectionsPerSecond = s.Advanced.LibtorrentOutgoingConnectionsPerSecond,
            LibtorrentUpnpLeaseDuration = s.Advanced.LibtorrentUpnpLeaseDuration,
            LibtorrentPeerTos = s.Advanced.LibtorrentPeerTos,
            LibtorrentUtpTcpMixedModeAlgorithm = s.Advanced.LibtorrentUtpTcpMixedModeAlgorithm switch {
                var x when x == UtpTcpMixedModeOptions[1] => QBittorrentUtpTcpMixedModeAlgorithm.PeerProportional,
                _ => QBittorrentUtpTcpMixedModeAlgorithm.PreferTcp
            },
            LibtorrentAllowMultipleConnectionsFromSameIp = s.Advanced.LibtorrentAllowMultipleConnectionsFromSameIp,
            LibtorrentEnableEmbeddedTracker = s.Advanced.LibtorrentEnableEmbeddedTracker,
            LibtorrentEmbeddedTrackerPort = s.Advanced.LibtorrentEmbeddedTrackerPort,
            EmbeddedTrackerPortForwarding = s.Advanced.EmbeddedTrackerPortForwarding,
            IgnoreSslErrors = s.Advanced.IgnoreSslErrors,
            PythonExecutablePath = s.Advanced.PythonExecutablePath,
            LibtorrentUploadSlotsBehavior = s.Advanced.LibtorrentUploadSlotsBehavior switch {
                var x when x == UploadSlotsChokingAlgorithmOptions[1] => QBittorrentChokingAlgorithm.RateBased,
                _ => QBittorrentChokingAlgorithm.FixedSlots
            },
            LibtorrentUploadChokingAlgorithm = s.Advanced.LibtorrentUploadChokingAlgorithm switch {
                var x when x == SeedChokingAlgorithmOptions[1] => QBittorrentSeedChokingAlgorithm.FastestUpload,
                var x when x == SeedChokingAlgorithmOptions[2] => QBittorrentSeedChokingAlgorithm.AntiLeech,
                _ => QBittorrentSeedChokingAlgorithm.RoundRobin
            },
            LibtorrentAnnounceToAllTrackers = s.Advanced.LibtorrentAnnounceToAllTrackers,
            LibtorrentAnnounceToAllTiers = s.Advanced.LibtorrentAnnounceToAllTiers,
            LibtorrentAnnounceIp = s.Advanced.LibtorrentAnnounceIp,
            LibtorrentMaxConcurrentHttpAnnounces = s.Advanced.LibtorrentMaxConcurrentHttpAnnounces,
            LibtorrentStopTrackerTimeout = s.Advanced.LibtorrentStopTrackerTimeout,
            LibtorrentValidateHttpsTrackerCertificate = s.Advanced.LibtorrentValidateHttpsTrackerCertificate,
            LibtorrentIdnSupportEnabled = s.Advanced.LibtorrentIdnSupportEnabled,
            LibtorrentSsrfMitigation = s.Advanced.LibtorrentSsrfMitigation,
            LibtorrentBlockPeersOnPrivilegedPorts = s.Advanced.LibtorrentBlockPeersOnPrivilegedPorts,
            LibtorrentAnnouncePort = s.Advanced.LibtorrentAnnouncePort,
            LibtorrentPeerTurnover = s.Advanced.LibtorrentPeerTurnover,
            LibtorrentPeerTurnoverCutoff = s.Advanced.LibtorrentPeerTurnoverCutoff,
            LibtorrentPeerTurnoverInterval = s.Advanced.LibtorrentPeerTurnoverInterval,
            LibtorrentRequestQueueSize = s.Advanced.LibtorrentRequestQueueSize,
            LibtorrentDhtBootstrapNodes = s.Advanced.LibtorrentDhtBootstrapNodes,
            LibtorrentI2PInboundQuantity = s.Advanced.LibtorrentI2pInboundQuantity,
            LibtorrentI2POutboundQuantity = s.Advanced.LibtorrentI2pOutboundQuantity,
            LibtorrentI2PInboundLength = s.Advanced.LibtorrentI2pInboundLength,
            LibtorrentI2POutboundLength = s.Advanced.LibtorrentI2pOutboundLength
        };

        foreach (var sd in s.Downloads.ScanDirectories) {
            if (!string.IsNullOrEmpty(sd.Path)) {
                ret.ScanDirectories[sd.Path] = sd.Action switch {
                    var x when x == ScanDirectoryActionOptions[0] => "0",
                    var x when x == ScanDirectoryActionOptions[1] => "1",
                    _ => sd.Action
                };
            }
        }

        if (!string.IsNullOrEmpty(s.Connection.BannedIpAddresses))
            ret.BannedIpAddresses.AddRange(s.Connection.BannedIpAddresses.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        if (!string.IsNullOrEmpty(s.BitTorrent.AdditionalTrackers))
            ret.AdditionalTrackers.AddRange(s.BitTorrent.AdditionalTrackers.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        if (!string.IsNullOrEmpty(s.Rss.RssSmartEpisodeFilters))
            ret.RssSmartEpisodeFilters.AddRange(s.Rss.RssSmartEpisodeFilters.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        if (!string.IsNullOrEmpty(s.WebUI.WebUICustomHttpHeaders))
            ret.WebUICustomHttpHeaders.AddRange(s.WebUI.WebUICustomHttpHeaders.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        if (!string.IsNullOrEmpty(s.WebUI.BypassAuthenticationSubnetWhitelist))
            ret.BypassAuthenticationSubnetWhitelist.AddRange(s.WebUI.BypassAuthenticationSubnetWhitelist.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        return ret;
    }
}
