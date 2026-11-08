using Google.Protobuf.WellKnownTypes;

using QBittorrent.Client;

using RTSharp.Daemon.Protocols.DataProvider.Settings;

using Grpc.Core;

namespace RTSharp.Daemon.Services.qbittorrent;

public class SettingsGrpc
{
    private readonly QbitClient Client;

    public SettingsGrpc([ServiceKey] string InstanceKey, IServiceProvider ServiceProvider)
    {
        Client = ServiceProvider.GetRequiredKeyedService<QbitClient>(InstanceKey);
    }

    public async Task<StringValue> GetDefaultSavePath()
    {
        var client = await Client.Init();

        return new StringValue { Value = await client.GetDefaultSavePathAsync() };
    }

    public async Task<QBittorrentSettings> GetSettings()
    {
        var client = await Client.Init();
        var prefs = await client.GetPreferencesAsync();
        return MapSettingsToProto(prefs);
    }

    public async Task SetSettings(QBittorrentSettings req)
    {
        var client = await Client.Init();
        var prefs = MapSettingsFromProto(req);
        await client.SetPreferencesAsync(prefs);
    }

    public async Task<QBittorrentNetworkInterfaces> GetNetworkInterfaces()
    {
        var client = await Client.Init();
        var ifaces = await client.GetNetworkInterfacesAsync();
        var result = new QBittorrentNetworkInterfaces();
        result.Interfaces.AddRange(ifaces.Select(i => new QBittorrentNetworkInterface { Id = i.Id, Name = i.Name }));
        return result;
    }

    public async Task<QBittorrentNetworkInterfaceAddresses> GetNetworkInterfaceAddresses(string interfaceId)
    {
        var client = await Client.Init();
        var addrs = await client.GetNetworkInterfaceAddressesAsync(interfaceId);
        var result = new QBittorrentNetworkInterfaceAddresses();
        result.Addresses.AddRange(addrs);
        return result;
    }

    public async Task<StringValue> RotateApiKey()
    {
        var client = await Client.Init();
        try {
            var key = await client.RotateApiKeyAsync();
            return new StringValue { Value = key };
        } catch (ApiNotSupportedException) {
            throw new RpcException(new Status(StatusCode.Unimplemented, "API keys are not supported by this version of qBittorrent"));
        }
    }

    public async Task DeleteApiKey()
    {
        var client = await Client.Init();
        try {
            await client.DeleteApiKeyAsync();
        } catch (ApiNotSupportedException) {
            throw new RpcException(new Status(StatusCode.Unimplemented, "API keys are not supported by this version of qBittorrent"));
        }
    }

    private static QBittorrentSettings MapSettingsToProto(Preferences p)
    {
        var r = new QBittorrentSettings {
            // Behaviour
            LogEnabled = p.LogEnabled,
            LogPath = p.LogPath,
            LogBackupEnabled = p.LogBackupEnabled,
            LogMaxSize = p.LogMaxSize,
            LogDeleteOld = p.LogDeleteOld,
            LogAge = p.LogAge,
            LogAgeType = p.LogAgeType,

            // Downloads
            TorrentContentLayout = p.TorrentContentLayout?.ToString(),
            AddToTopOfQueue = p.AddToTopOfQueue,
            AddTorrentPaused = p.AddTorrentPaused,
            TorrentStopCondition = p.TorrentStopCondition,
            MergeTrackers = p.MergeTrackers,
            AutoDeleteMode = (int?)p.TorrentFileAutoDeleteMode,
            PreallocateAll = p.PreallocateAll,
            AppendExtensionToIncompleteFiles = p.AppendExtensionToIncompleteFiles,
            UseUnwantedFolder = p.UseUnwantedFolder,
            AutoTMMEnabledByDefault = p.AutoTMMEnabledByDefault,
            AutoTMMRetainedWhenCategoryChanges = p.AutoTMMRetainedWhenCategoryChanges,
            AutoTMMRetainedWhenDefaultSavePathChanges = p.AutoTMMRetainedWhenDefaultSavePathChanges,
            AutoTMMRetainedWhenCategorySavePathChanges = p.AutoTMMRetainedWhenCategorySavePathChanges,
            UseSubcategories = p.UseSubcategories,
            UseCategoryPathsInManualMode = p.UseCategoryPathsInManualMode,
            SavePath = p.SavePath,
            TempPathEnabled = p.TempPathEnabled,
            TempPath = p.TempPath,
            ExportDirectory = p.ExportDirectory,
            ExportDirectoryForFinished = p.ExportDirectoryForFinished,
            ExcludedFileNamesEnabled = p.ExcludedFileNamesEnabled,
            ExcludedFileNames = p.ExcludedFileNames,
            MailNotificationEnabled = p.MailNotificationEnabled,
            MailNotificationSender = p.MailNotificationSender,
            MailNotificationEmailAddress = p.MailNotificationEmailAddress,
            MailNotificationSmtpServer = p.MailNotificationSmtpServer,
            MailNotificationSslEnabled = p.MailNotificationSslEnabled,
            MailNotificationAuthenticationEnabled = p.MailNotificationAuthenticationEnabled,
            MailNotificationUsername = p.MailNotificationUsername,
            MailNotificationPassword = p.MailNotificationPassword,
            AutorunEnabled = p.AutorunEnabled,
            AutorunProgram = p.AutorunProgram,
            AutorunOnTorrentAddedEnabled = p.AutorunOnTorrentAddedEnabled,
            AutorunOnTorrentAddedProgram = p.AutorunOnTorrentAddedProgram,

            // Connection
            BittorrentProtocol = p.BittorrentProtocol switch {
                QBittorrent.Client.BittorrentProtocol.Tcp => QBittorrentBittorrentProtocol.Tcp,
                QBittorrent.Client.BittorrentProtocol.uTP => QBittorrentBittorrentProtocol.Utp,
                _ => QBittorrentBittorrentProtocol.Both
            },
            ListenPort = p.ListenPort,
            UpnpEnabled = p.UpnpEnabled,
            RandomPort = p.RandomPort,
            MaxConnections = p.MaxConnections,
            MaxConnectionsPerTorrent = p.MaxConnectionsPerTorrent,
            MaxUploads = p.MaxUploads,
            MaxUploadsPerTorrent = p.MaxUploadsPerTorrent,
            ProxyType = p.ProxyType switch {
                QBittorrent.Client.ProxyType.Http => QBittorrentProxyType.Http,
                QBittorrent.Client.ProxyType.Socks5 => QBittorrentProxyType.Socks5,
                QBittorrent.Client.ProxyType.HttpAuth => QBittorrentProxyType.HttpAuth,
                QBittorrent.Client.ProxyType.Socks5Auth => QBittorrentProxyType.Socks5Auth,
                QBittorrent.Client.ProxyType.Socks4 => QBittorrentProxyType.Socks4,
                _ => QBittorrentProxyType.None
            },
            ProxyAddress = p.ProxyAddress,
            ProxyPort = p.ProxyPort,
            ProxyHostnameLookup = p.ProxyHostnameLookup,
            ProxyAuthenticationEnabled = p.ProxyType != ProxyType.None,
            ProxyUsername = p.ProxyUsername,
            ProxyPassword = p.ProxyPassword,
            ProxyBittorrent = p.ProxyBittorrent,
            ProxyPeerConnections = p.ProxyPeerConnections,
            ProxyRss = p.ProxyRss,
            ProxyMisc = p.ProxyMisc,
            IpFilterEnabled = p.IpFilterEnabled,
            IpFilterPath = p.IpFilterPath,
            IpFilterTrackers = p.IpFilterTrackers,

            // Speed
            UploadLimit = p.UploadLimit,
            DownloadLimit = p.DownloadLimit,
            AlternativeUploadLimit = p.AlternativeUploadLimit,
            AlternativeDownloadLimit = p.AlternativeDownloadLimit,
            SchedulerEnabled = p.SchedulerEnabled,
            ScheduleFromHour = p.ScheduleFromHour,
            ScheduleFromMinute = p.ScheduleFromMinute,
            ScheduleToHour = p.ScheduleToHour,
            ScheduleToMinute = p.ScheduleToMinute,
            SchedulerDays = p.SchedulerDays switch {
                QBittorrent.Client.SchedulerDay.Weekday => QBittorrentSchedulerDay.Weekday,
                QBittorrent.Client.SchedulerDay.Weekend => QBittorrentSchedulerDay.Weekend,
                QBittorrent.Client.SchedulerDay.Monday => QBittorrentSchedulerDay.Monday,
                QBittorrent.Client.SchedulerDay.Tuesday => QBittorrentSchedulerDay.Tuesday,
                QBittorrent.Client.SchedulerDay.Wednesday => QBittorrentSchedulerDay.Wednesday,
                QBittorrent.Client.SchedulerDay.Thursday => QBittorrentSchedulerDay.Thursday,
                QBittorrent.Client.SchedulerDay.Friday => QBittorrentSchedulerDay.Friday,
                QBittorrent.Client.SchedulerDay.Saturday => QBittorrentSchedulerDay.Saturday,
                QBittorrent.Client.SchedulerDay.Sunday => QBittorrentSchedulerDay.Sunday,
                _ => QBittorrentSchedulerDay.Every
            },
            LimitUTPRate = p.LimitUTPRate,
            LimitTcpOverhead = p.LimitTcpOverhead,
            LimitLAN = p.LimitLAN,

            // BitTorrent
            DHT = p.DHT,
            PeerExchange = p.PeerExchange,
            LocalPeerDiscovery = p.LocalPeerDiscovery,
            Encryption = p.Encryption switch {
                QBittorrent.Client.Encryption.ForceOn => QBittorrentEncryption.ForceOn,
                QBittorrent.Client.Encryption.ForceOff => QBittorrentEncryption.ForceOff,
                _ => QBittorrentEncryption.Prefer
            },
            AnonymousMode = p.AnonymousMode,
            QueueingEnabled = p.QueueingEnabled,
            MaxActiveDownloads = p.MaxActiveDownloads,
            MaxActiveUploads = p.MaxActiveUploads,
            MaxActiveTorrents = p.MaxActiveTorrents,
            DoNotCountSlowTorrents = p.DoNotCountSlowTorrents,
            SlowTorrentDownloadRateThreshold = p.SlowTorrentDownloadRateThreshold,
            SlowTorrentUploadRateThreshold = p.SlowTorrentUploadRateThreshold,
            SlowTorrentInactiveTime = p.SlowTorrentInactiveTime,
            MaxRatioEnabled = p.MaxRatioEnabled,
            MaxRatio = p.MaxRatio,
            MaxSeedingTimeEnabled = p.MaxSeedingTimeEnabled,
            MaxSeedingTime = p.MaxSeedingTime,
            MaxInactiveSeedingTimeEnabled = p.MaxInactiveSeedingTimeEnabled,
            MaxInactiveSeedingTime = p.MaxInactiveSeedingTime,
            MaxRatioAction = (int?)p.MaxRatioAction switch {
                1 => QBittorrentMaxRatioAction.Remove,
                2 => QBittorrentMaxRatioAction.EnableSuperSeeding,
                3 => QBittorrentMaxRatioAction.RemoveWithContent,
                _ => QBittorrentMaxRatioAction.Stop
            },
            AdditionalTrackersEnabled = p.AdditionalTrackersEnabled,
            TrackerListEnabled = p.TrackerListEnabled,
            TrackerListUrl = p.TrackerListUrl,
            TrackerListContent = p.TrackerListContent,

            // RSS
            RssProcessingEnabled = p.RssProcessingEnabled,
            RssRefreshInterval = p.RssRefreshInterval,
            RssMaxArticlesPerFeed = p.RssMaxArticlesPerFeed,
            RssAutoDownloadingEnabled = p.RssAutoDownloadingEnabled,
            RssDownloadRepackProperEpisodes = p.RssDownloadRepackProperEpisodes,
            RssFetchDelay = p.RssFetchDelay,

            // WebUI
            WebUIAddress = p.WebUIAddress,
            WebUIPort = p.WebUIPort,
            WebUIUpnp = p.WebUIUpnp,
            WebUIUsername = p.WebUIUsername,
            WebUIPasswordHash = p.WebUIPasswordHash,
            WebUIApiKey = String.IsNullOrEmpty(p.WebUIApiKey) ? null : p.WebUIApiKey,
            WebUIHttps = p.WebUIHttps,
            WebUISslKeyPath = p.WebUISslKeyPath,
            WebUISslCertificatePath = p.WebUISslCertificatePath,
            WebUIClickjackingProtection = p.WebUIClickjackingProtection,
            WebUICsrfProtection = p.WebUICsrfProtection,
            WebUISecureCookie = p.WebUISecureCookie,
            WebUIMaxAuthenticationFailures = p.WebUIMaxAuthenticationFailures,
            WebUIBanDuration = p.WebUIBanDuration,
            WebUISessionTimeout = p.WebUISessionTimeout,
            AlternativeWebUIEnabled = p.AlternativeWebUIEnabled,
            AlternativeWebUIPath = p.AlternativeWebUIPath,
            WebUIHostHeaderValidation = p.WebUIHostHeaderValidation,
            WebUIDomain = p.WebUIDomain,
            WebUICustomHttpHeadersEnabled = p.WebUICustomHttpHeadersEnabled,
            BypassLocalAuthentication = p.BypassLocalAuthentication,
            BypassAuthenticationSubnetWhitelistEnabled = p.BypassAuthenticationSubnetWhitelistEnabled,
            ReverseProxyEnabled = p.ReverseProxyEnabled,
            ReverseProxiesList = p.ReverseProxiesList,
            DynamicDnsEnabled = p.DynamicDnsEnabled,
            DynamicDnsService = p.DynamicDnsService switch {
                QBittorrent.Client.DynamicDnsService.DynDNS => QBittorrentDynamicDnsService.Dyndns,
                QBittorrent.Client.DynamicDnsService.NoIP => QBittorrentDynamicDnsService.Noip,
                _ => QBittorrentDynamicDnsService.None
            },
            DynamicDnsUsername = p.DynamicDnsUsername,
            DynamicDnsPassword = p.DynamicDnsPassword,
            DynamicDnsDomain = p.DynamicDnsDomain,

            // Advanced
            ResumeDataStorageType = p.ResumeDataStorageType,
            TorrentContentRemoveOption = p.TorrentContentRemoveOption,
            CurrentNetworkInterface = p.CurrentNetworkInterface,
            CurrentInterfaceAddress = p.CurrentInterfaceAddress,
            SaveResumeDataInterval = p.SaveResumeDataInterval,
            RecheckCompletedTorrents = p.RecheckCompletedTorrents,
            ResolvePeerCountries = p.ResolvePeerCountries,
            MemoryWorkingSetLimit = p.MemoryWorkingSetLimit,
            SaveStatisticsInterval = p.SaveStatisticsInterval,
            TorrentFileSizeLimit = p.TorrentFileSizeLimit is long tfsz ? (int)(tfsz / (1024 * 1024)) : (int?)null,
            ConfirmTorrentRecheck = p.ConfirmTorrentRecheck,
            AppInstanceName = p.AppInstanceName,
            RefreshInterval = p.RefreshInterval,
            ReannounceWhenAddressChanged = p.ReannounceWhenAddressChanged,
            EmbeddedTrackerPortForwarding = p.EmbeddedTrackerPortForwarding,
            IgnoreSslErrors = p.IgnoreSslErrors,
            PythonExecutablePath = p.PythonExecutablePath,
            LibtorrentAsynchronousIOThreads = p.LibtorrentAsynchronousIOThreads,
            LibtorrentFilePoolSize = p.LibtorrentFilePoolSize,
            LibtorrentOutstandingMemoryWhenCheckingTorrent = p.LibtorrentOutstandingMemoryWhenCheckingTorrent,
            LibtorrentDiskCache = p.LibtorrentDiskCache,
            LibtorrentDiskCacheExpiryInterval = p.LibtorrentDiskCacheExpiryInterval,
            LibtorrentDiskIOReadMode = p.LibtorrentDiskIOReadMode,
            LibtorrentDiskIOWriteMode = p.LibtorrentDiskIOWriteMode,
            LibtorrentHashingThreads = p.LibtorrentHashingThreads,
            LibtorrentDiskQueueSize = p.LibtorrentDiskQueueSize is long dqs ? (int)(dqs / 1024) : (int?)null,
            LibtorrentBdecodeDepthLimit = p.LibtorrentBdecodeDepthLimit,
            LibtorrentBdecodeTokenLimit = p.LibtorrentBdecodeTokenLimit,
            LibtorrentCoalesceReadsAndWrites = p.LibtorrentCoalesceReadsAndWrites,
            LibtorrentPieceExtentAffinity = p.LibtorrentPieceExtentAffinity,
            LibtorrentSendUploadPieceSuggestions = p.LibtorrentSendUploadPieceSuggestions,
            LibtorrentSendBufferWatermark = p.LibtorrentSendBufferWatermark,
            LibtorrentSendBufferLowWatermark = p.LibtorrentSendBufferLowWatermark,
            LibtorrentSendBufferWatermarkFactor = p.LibtorrentSendBufferWatermarkFactor,
            LibtorrentSocketBacklogSize = p.LibtorrentSocketBacklogSize,
            LibtorrentSocketSendBufferSize = p.LibtorrentSocketSendBufferSize,
            LibtorrentSocketReceiveBufferSize = p.LibtorrentSocketReceiveBufferSize,
            LibtorrentOutgoingPortsMin = p.LibtorrentOutgoingPortsMin,
            LibtorrentOutgoingPortsMax = p.LibtorrentOutgoingPortsMax,
            LibtorrentOutgoingConnectionsPerSecond = p.LibtorrentOutgoingConnectionsPerSecond,
            LibtorrentUpnpLeaseDuration = p.LibtorrentUpnpLeaseDuration,
            LibtorrentPeerTos = p.LibtorrentPeerTos,
            LibtorrentUtpTcpMixedModeAlgorithm = p.LibtorrentUtpTcpMixedModeAlgorithm switch {
                QBittorrent.Client.UtpTcpMixedModeAlgorithm.PeerProportional => QBittorrentUtpTcpMixedModeAlgorithm.PeerProportional,
                _ => QBittorrentUtpTcpMixedModeAlgorithm.PreferTcp
            },
            LibtorrentAllowMultipleConnectionsFromSameIp = p.LibtorrentAllowMultipleConnectionsFromSameIp,
            LibtorrentEnableEmbeddedTracker = p.LibtorrentEnableEmbeddedTracker,
            LibtorrentEmbeddedTrackerPort = p.LibtorrentEmbeddedTrackerPort,
            LibtorrentUploadSlotsBehavior = p.LibtorrentUploadSlotsBehavior switch {
                QBittorrent.Client.ChokingAlgorithm.RateBased => QBittorrentChokingAlgorithm.RateBased,
                _ => QBittorrentChokingAlgorithm.FixedSlots
            },
            LibtorrentUploadChokingAlgorithm = p.LibtorrentUploadChokingAlgorithm switch {
                QBittorrent.Client.SeedChokingAlgorithm.FastestUpload => QBittorrentSeedChokingAlgorithm.FastestUpload,
                QBittorrent.Client.SeedChokingAlgorithm.AntiLeech => QBittorrentSeedChokingAlgorithm.AntiLeech,
                _ => QBittorrentSeedChokingAlgorithm.RoundRobin
            },
            LibtorrentAnnounceToAllTrackers = p.LibtorrentAnnounceToAllTrackers,
            LibtorrentAnnounceToAllTiers = p.LibtorrentAnnounceToAllTiers,
            LibtorrentAnnounceIp = p.LibtorrentAnnounceIp,
            LibtorrentStopTrackerTimeout = p.LibtorrentStopTrackerTimeout,
            LibtorrentMaxConcurrentHttpAnnounces = p.LibtorrentMaxConcurrentHttpAnnounces,
            LibtorrentIdnSupportEnabled = p.LibtorrentIdnSupportEnabled,
            LibtorrentValidateHttpsTrackerCertificate = p.LibtorrentValidateHttpsTrackerCertificate,
            LibtorrentSsrfMitigation = p.LibtorrentSsrfMitigation,
            LibtorrentBlockPeersOnPrivilegedPorts = p.LibtorrentBlockPeersOnPrivilegedPorts,
            LibtorrentAnnouncePort = p.LibtorrentAnnouncePort,
            LibtorrentPeerTurnover = p.LibtorrentPeerTurnover,
            LibtorrentPeerTurnoverCutoff = p.LibtorrentPeerTurnoverCutoff,
            LibtorrentPeerTurnoverInterval = p.LibtorrentPeerTurnoverInterval,
            LibtorrentRequestQueueSize = p.LibtorrentRequestQueueSize,
            LibtorrentDhtBootstrapNodes = p.LibtorrentDhtBootstrapNodes,
            LibtorrentI2PInboundQuantity = p.LibtorrentI2PInboundQuantity,
            LibtorrentI2POutboundQuantity = p.LibtorrentI2POutboundQuantity,
            LibtorrentI2PInboundLength = p.LibtorrentI2PInboundLength,
            LibtorrentI2POutboundLength = p.LibtorrentI2POutboundLength
        };

        // Downloads
        if (p.ScanDirectories != null)
            foreach (var (path, loc) in p.ScanDirectories)
                r.ScanDirectories[path] = loc.CustomFolder ?? (loc.StandardFolder == StandardSaveLocation.MonitoredFolder ? "0" : "1");
        // Connection
        if (p.BannedIpAddresses != null) r.BannedIpAddresses.AddRange(p.BannedIpAddresses);
        // BitTorrent
        if (p.AdditinalTrackers != null) r.AdditionalTrackers.AddRange(p.AdditinalTrackers);
        // RSS
        if (p.RssSmartEpisodeFilters != null) r.RssSmartEpisodeFilters.AddRange(p.RssSmartEpisodeFilters);
        // WebUI
        if (p.WebUICustomHttpHeaders != null) r.WebUICustomHttpHeaders.AddRange(p.WebUICustomHttpHeaders);
        if (p.BypassAuthenticationSubnetWhitelist != null) r.BypassAuthenticationSubnetWhitelist.AddRange(p.BypassAuthenticationSubnetWhitelist);

        return r;
    }

    private static Preferences MapSettingsFromProto(QBittorrentSettings r)
    {
        var p = new Preferences {
            // Behaviour
            LogEnabled = r.LogEnabled,
            LogPath = r.LogPath,
            LogBackupEnabled = r.LogBackupEnabled,
            LogMaxSize = r.LogMaxSize,
            LogDeleteOld = r.LogDeleteOld,
            LogAge = r.LogAge,
            LogAgeType = r.LogAgeType,

            // Downloads
            TorrentContentLayout = System.Enum.TryParse<QBittorrent.Client.TorrentContentLayout>(r.TorrentContentLayout, out var layout) ? layout : null,
            AddToTopOfQueue = r.AddToTopOfQueue,
            TorrentStopCondition = r.TorrentStopCondition,
            MergeTrackers = r.MergeTrackers,
            TorrentFileAutoDeleteMode = (QBittorrent.Client.TorrentFileAutoDeleteMode?)r.AutoDeleteMode,
            UseUnwantedFolder = r.UseUnwantedFolder,
            UseCategoryPathsInManualMode = r.UseCategoryPathsInManualMode,
            UseSubcategories = r.UseSubcategories,
            ExcludedFileNamesEnabled = r.ExcludedFileNamesEnabled,
            ExcludedFileNames = r.ExcludedFileNames,
            AutorunOnTorrentAddedEnabled = r.AutorunOnTorrentAddedEnabled,
            AutorunOnTorrentAddedProgram = r.AutorunOnTorrentAddedProgram,
            AddTorrentPaused = r.AddTorrentPaused,
            PreallocateAll = r.PreallocateAll,
            AppendExtensionToIncompleteFiles = r.AppendExtensionToIncompleteFiles,
            AutoTMMEnabledByDefault = r.AutoTMMEnabledByDefault,
            AutoTMMRetainedWhenCategoryChanges = r.AutoTMMRetainedWhenCategoryChanges,
            AutoTMMRetainedWhenDefaultSavePathChanges = r.AutoTMMRetainedWhenDefaultSavePathChanges,
            AutoTMMRetainedWhenCategorySavePathChanges = r.AutoTMMRetainedWhenCategorySavePathChanges,
            SavePath = r.SavePath,
            TempPathEnabled = r.TempPathEnabled,
            TempPath = r.TempPath,
            ExportDirectory = r.ExportDirectory,
            ExportDirectoryForFinished = r.ExportDirectoryForFinished,
            MailNotificationEnabled = r.MailNotificationEnabled,
            MailNotificationSender = r.MailNotificationSender,
            MailNotificationEmailAddress = r.MailNotificationEmailAddress,
            MailNotificationSmtpServer = r.MailNotificationSmtpServer,
            MailNotificationSslEnabled = r.MailNotificationSslEnabled,
            MailNotificationAuthenticationEnabled = r.MailNotificationAuthenticationEnabled,
            MailNotificationUsername = r.MailNotificationUsername,
            MailNotificationPassword = r.MailNotificationPassword,
            AutorunEnabled = r.AutorunEnabled,
            AutorunProgram = r.AutorunProgram,

            // Connection
            BittorrentProtocol = r.BittorrentProtocol switch {
                QBittorrentBittorrentProtocol.Tcp => QBittorrent.Client.BittorrentProtocol.Tcp,
                QBittorrentBittorrentProtocol.Utp => QBittorrent.Client.BittorrentProtocol.uTP,
                _ => QBittorrent.Client.BittorrentProtocol.Both
            },
            ListenPort = r.ListenPort,
            UpnpEnabled = r.UpnpEnabled,
            RandomPort = r.RandomPort,
            MaxConnections = r.MaxConnections,
            MaxConnectionsPerTorrent = r.MaxConnectionsPerTorrent,
            MaxUploads = r.MaxUploads,
            MaxUploadsPerTorrent = r.MaxUploadsPerTorrent,
            ProxyType = r.ProxyType switch {
                QBittorrentProxyType.Http => QBittorrent.Client.ProxyType.Http,
                QBittorrentProxyType.Socks5 => QBittorrent.Client.ProxyType.Socks5,
                QBittorrentProxyType.HttpAuth => QBittorrent.Client.ProxyType.HttpAuth,
                QBittorrentProxyType.Socks5Auth => QBittorrent.Client.ProxyType.Socks5Auth,
                QBittorrentProxyType.Socks4 => QBittorrent.Client.ProxyType.Socks4,
                _ => QBittorrent.Client.ProxyType.None
            },
            ProxyAddress = r.ProxyAddress,
            ProxyPort = r.ProxyPort,
            ProxyHostnameLookup = r.ProxyHostnameLookup,
#pragma warning disable CS0618 // Type or member is obsolete
            ProxyAuthenticationEnabled = r.ProxyAuthenticationEnabled,
#pragma warning restore CS0618 // Type or member is obsolete
            ProxyUsername = r.ProxyUsername,
            ProxyPassword = r.ProxyPassword,
            ProxyBittorrent = r.ProxyBittorrent,
            ProxyPeerConnections = r.ProxyPeerConnections,
            ProxyRss = r.ProxyRss,
            ProxyMisc = r.ProxyMisc,
            IpFilterEnabled = r.IpFilterEnabled,
            IpFilterPath = r.IpFilterPath,
            IpFilterTrackers = r.IpFilterTrackers,
            BannedIpAddresses = r.BannedIpAddresses.ToList(),

            // Speed
            DownloadLimit = r.DownloadLimit,
            UploadLimit = r.UploadLimit,
            AlternativeDownloadLimit = r.AlternativeDownloadLimit,
            AlternativeUploadLimit = r.AlternativeUploadLimit,
            SchedulerEnabled = r.SchedulerEnabled,
            ScheduleFromHour = r.ScheduleFromHour,
            ScheduleFromMinute = r.ScheduleFromMinute,
            ScheduleToHour = r.ScheduleToHour,
            ScheduleToMinute = r.ScheduleToMinute,
            SchedulerDays = r.SchedulerDays switch {
                QBittorrentSchedulerDay.Weekday => QBittorrent.Client.SchedulerDay.Weekday,
                QBittorrentSchedulerDay.Weekend => QBittorrent.Client.SchedulerDay.Weekend,
                QBittorrentSchedulerDay.Monday => QBittorrent.Client.SchedulerDay.Monday,
                QBittorrentSchedulerDay.Tuesday => QBittorrent.Client.SchedulerDay.Tuesday,
                QBittorrentSchedulerDay.Wednesday => QBittorrent.Client.SchedulerDay.Wednesday,
                QBittorrentSchedulerDay.Thursday => QBittorrent.Client.SchedulerDay.Thursday,
                QBittorrentSchedulerDay.Friday => QBittorrent.Client.SchedulerDay.Friday,
                QBittorrentSchedulerDay.Saturday => QBittorrent.Client.SchedulerDay.Saturday,
                QBittorrentSchedulerDay.Sunday => QBittorrent.Client.SchedulerDay.Sunday,
                _ => QBittorrent.Client.SchedulerDay.Every
            },
            LimitUTPRate = r.LimitUTPRate,
            LimitTcpOverhead = r.LimitTcpOverhead,
            LimitLAN = r.LimitLAN,

            // BitTorrent
            DHT = r.DHT,
            PeerExchange = r.PeerExchange,
            LocalPeerDiscovery = r.LocalPeerDiscovery,
            Encryption = r.Encryption switch {
                QBittorrentEncryption.ForceOn => QBittorrent.Client.Encryption.ForceOn,
                QBittorrentEncryption.ForceOff => QBittorrent.Client.Encryption.ForceOff,
                _ => QBittorrent.Client.Encryption.Prefer
            },
            AnonymousMode = r.AnonymousMode,
            QueueingEnabled = r.QueueingEnabled,
            MaxActiveDownloads = r.MaxActiveDownloads,
            MaxActiveUploads = r.MaxActiveUploads,
            MaxActiveTorrents = r.MaxActiveTorrents,
            DoNotCountSlowTorrents = r.DoNotCountSlowTorrents,
            SlowTorrentDownloadRateThreshold = r.SlowTorrentDownloadRateThreshold,
            SlowTorrentUploadRateThreshold = r.SlowTorrentUploadRateThreshold,
            SlowTorrentInactiveTime = r.SlowTorrentInactiveTime,
            MaxRatioEnabled = r.MaxRatioEnabled,
            MaxRatio = r.MaxRatio,
            MaxSeedingTimeEnabled = r.MaxSeedingTimeEnabled,
            MaxSeedingTime = r.MaxSeedingTime,
            MaxInactiveSeedingTimeEnabled = r.MaxInactiveSeedingTimeEnabled,
            MaxInactiveSeedingTime = r.MaxInactiveSeedingTime,
            MaxRatioAction = (QBittorrent.Client.MaxRatioAction)(int)r.MaxRatioAction,
            AdditionalTrackersEnabled = r.AdditionalTrackersEnabled,
            AdditinalTrackers = r.AdditionalTrackers.ToList(),
            TrackerListEnabled = r.TrackerListEnabled,
            TrackerListUrl = r.TrackerListUrl,

            // RSS
            RssProcessingEnabled = r.RssProcessingEnabled,
            RssRefreshInterval = r.RssRefreshInterval,
            RssMaxArticlesPerFeed = r.RssMaxArticlesPerFeed,
            RssAutoDownloadingEnabled = r.RssAutoDownloadingEnabled,
            RssDownloadRepackProperEpisodes = r.RssDownloadRepackProperEpisodes,
            RssSmartEpisodeFilters = r.RssSmartEpisodeFilters.ToList(),
            RssFetchDelay = r.RssFetchDelay,

            // WebUI
            WebUIAddress = r.WebUIAddress,
            WebUIPort = r.WebUIPort,
            WebUIUpnp = r.WebUIUpnp,
            WebUIUsername = r.WebUIUsername,
            WebUIPassword = r.WebUIPassword,
            WebUIHttps = r.WebUIHttps,
            WebUISslKeyPath = r.WebUISslKeyPath,
            WebUISslCertificatePath = r.WebUISslCertificatePath,
            WebUIClickjackingProtection = r.WebUIClickjackingProtection,
            WebUICsrfProtection = r.WebUICsrfProtection,
            WebUISecureCookie = r.WebUISecureCookie,
            WebUIMaxAuthenticationFailures = r.WebUIMaxAuthenticationFailures,
            WebUIBanDuration = r.WebUIBanDuration,
            WebUISessionTimeout = r.WebUISessionTimeout,
            AlternativeWebUIEnabled = r.AlternativeWebUIEnabled,
            AlternativeWebUIPath = r.AlternativeWebUIPath,
            WebUIHostHeaderValidation = r.WebUIHostHeaderValidation,
            WebUIDomain = r.WebUIDomain,
            WebUICustomHttpHeadersEnabled = r.WebUICustomHttpHeadersEnabled,
            WebUICustomHttpHeaders = r.WebUICustomHttpHeaders.ToList(),
            BypassLocalAuthentication = r.BypassLocalAuthentication,
            BypassAuthenticationSubnetWhitelistEnabled = r.BypassAuthenticationSubnetWhitelistEnabled,
            BypassAuthenticationSubnetWhitelist = r.BypassAuthenticationSubnetWhitelist.ToList(),
            ReverseProxyEnabled = r.ReverseProxyEnabled,
            ReverseProxiesList = r.ReverseProxiesList,
            DynamicDnsEnabled = r.DynamicDnsEnabled,
            DynamicDnsService = r.DynamicDnsService switch {
                QBittorrentDynamicDnsService.Dyndns => QBittorrent.Client.DynamicDnsService.DynDNS,
                QBittorrentDynamicDnsService.Noip => QBittorrent.Client.DynamicDnsService.NoIP,
                _ => QBittorrent.Client.DynamicDnsService.None
            },
            DynamicDnsUsername = r.DynamicDnsUsername,
            DynamicDnsPassword = r.DynamicDnsPassword,
            DynamicDnsDomain = r.DynamicDnsDomain,

            // Advanced
            CurrentNetworkInterface = r.CurrentNetworkInterface,
            CurrentInterfaceAddress = r.CurrentInterfaceAddress,
            SaveResumeDataInterval = r.SaveResumeDataInterval,
            RecheckCompletedTorrents = r.RecheckCompletedTorrents,
            ResolvePeerCountries = r.ResolvePeerCountries,
            LibtorrentAsynchronousIOThreads = r.LibtorrentAsynchronousIOThreads,
            LibtorrentFilePoolSize = r.LibtorrentFilePoolSize,
            LibtorrentOutstandingMemoryWhenCheckingTorrent = r.LibtorrentOutstandingMemoryWhenCheckingTorrent,
            LibtorrentDiskCache = r.LibtorrentDiskCache,
            LibtorrentDiskCacheExpiryInterval = r.LibtorrentDiskCacheExpiryInterval,
            LibtorrentCoalesceReadsAndWrites = r.LibtorrentCoalesceReadsAndWrites,
            LibtorrentPieceExtentAffinity = r.LibtorrentPieceExtentAffinity,
            LibtorrentSendUploadPieceSuggestions = r.LibtorrentSendUploadPieceSuggestions,
            LibtorrentSendBufferWatermark = r.LibtorrentSendBufferWatermark,
            LibtorrentSendBufferLowWatermark = r.LibtorrentSendBufferLowWatermark,
            LibtorrentSendBufferWatermarkFactor = r.LibtorrentSendBufferWatermarkFactor,
            LibtorrentSocketBacklogSize = r.LibtorrentSocketBacklogSize,
            LibtorrentOutgoingPortsMin = r.LibtorrentOutgoingPortsMin,
            LibtorrentOutgoingPortsMax = r.LibtorrentOutgoingPortsMax,
            LibtorrentUtpTcpMixedModeAlgorithm = r.LibtorrentUtpTcpMixedModeAlgorithm switch {
                QBittorrentUtpTcpMixedModeAlgorithm.PeerProportional => QBittorrent.Client.UtpTcpMixedModeAlgorithm.PeerProportional,
                _ => QBittorrent.Client.UtpTcpMixedModeAlgorithm.PreferTcp
            },
            LibtorrentAllowMultipleConnectionsFromSameIp = r.LibtorrentAllowMultipleConnectionsFromSameIp,
            LibtorrentEnableEmbeddedTracker = r.LibtorrentEnableEmbeddedTracker,
            LibtorrentEmbeddedTrackerPort = r.LibtorrentEmbeddedTrackerPort,
            LibtorrentUploadSlotsBehavior = r.LibtorrentUploadSlotsBehavior switch {
                QBittorrentChokingAlgorithm.RateBased => QBittorrent.Client.ChokingAlgorithm.RateBased,
                _ => QBittorrent.Client.ChokingAlgorithm.FixedSlots
            },
            LibtorrentUploadChokingAlgorithm = r.LibtorrentUploadChokingAlgorithm switch {
                QBittorrentSeedChokingAlgorithm.FastestUpload => QBittorrent.Client.SeedChokingAlgorithm.FastestUpload,
                QBittorrentSeedChokingAlgorithm.AntiLeech => QBittorrent.Client.SeedChokingAlgorithm.AntiLeech,
                _ => QBittorrent.Client.SeedChokingAlgorithm.RoundRobin
            },
            LibtorrentAnnounceToAllTrackers = r.LibtorrentAnnounceToAllTrackers,
            LibtorrentAnnounceToAllTiers = r.LibtorrentAnnounceToAllTiers,
            LibtorrentAnnounceIp = r.LibtorrentAnnounceIp,
            LibtorrentStopTrackerTimeout = r.LibtorrentStopTrackerTimeout,
            LibtorrentMaxConcurrentHttpAnnounces = r.LibtorrentMaxConcurrentHttpAnnounces,
            ResumeDataStorageType = r.ResumeDataStorageType,
            TorrentContentRemoveOption = r.TorrentContentRemoveOption,
            MemoryWorkingSetLimit = r.MemoryWorkingSetLimit,
            SaveStatisticsInterval = r.SaveStatisticsInterval,
            TorrentFileSizeLimit = (long?)r.TorrentFileSizeLimit * 1024 * 1024,
            ConfirmTorrentRecheck = r.ConfirmTorrentRecheck,
            AppInstanceName = r.AppInstanceName,
            RefreshInterval = r.RefreshInterval,
            ReannounceWhenAddressChanged = r.ReannounceWhenAddressChanged,
            EmbeddedTrackerPortForwarding = r.EmbeddedTrackerPortForwarding,
            IgnoreSslErrors = r.IgnoreSslErrors,
            PythonExecutablePath = r.PythonExecutablePath,
            LibtorrentDiskIOReadMode = r.LibtorrentDiskIOReadMode,
            LibtorrentDiskIOWriteMode = r.LibtorrentDiskIOWriteMode,
            LibtorrentHashingThreads = r.LibtorrentHashingThreads,
            LibtorrentDiskQueueSize = (long?)r.LibtorrentDiskQueueSize * 1024,
            LibtorrentBdecodeDepthLimit = r.LibtorrentBdecodeDepthLimit,
            LibtorrentBdecodeTokenLimit = r.LibtorrentBdecodeTokenLimit,
            LibtorrentSocketSendBufferSize = r.LibtorrentSocketSendBufferSize,
            LibtorrentSocketReceiveBufferSize = r.LibtorrentSocketReceiveBufferSize,
            LibtorrentOutgoingConnectionsPerSecond = r.LibtorrentOutgoingConnectionsPerSecond,
            LibtorrentUpnpLeaseDuration = r.LibtorrentUpnpLeaseDuration,
            LibtorrentPeerTos = r.LibtorrentPeerTos,
            LibtorrentIdnSupportEnabled = r.LibtorrentIdnSupportEnabled,
            LibtorrentValidateHttpsTrackerCertificate = r.LibtorrentValidateHttpsTrackerCertificate,
            LibtorrentSsrfMitigation = r.LibtorrentSsrfMitigation,
            LibtorrentBlockPeersOnPrivilegedPorts = r.LibtorrentBlockPeersOnPrivilegedPorts,
            LibtorrentAnnouncePort = r.LibtorrentAnnouncePort,
            LibtorrentPeerTurnover = r.LibtorrentPeerTurnover,
            LibtorrentPeerTurnoverCutoff = r.LibtorrentPeerTurnoverCutoff,
            LibtorrentPeerTurnoverInterval = r.LibtorrentPeerTurnoverInterval,
            LibtorrentRequestQueueSize = r.LibtorrentRequestQueueSize,
            LibtorrentDhtBootstrapNodes = r.LibtorrentDhtBootstrapNodes,
            LibtorrentI2PInboundQuantity = r.LibtorrentI2PInboundQuantity,
            LibtorrentI2POutboundQuantity = r.LibtorrentI2POutboundQuantity,
            LibtorrentI2PInboundLength = r.LibtorrentI2PInboundLength,
            LibtorrentI2POutboundLength = r.LibtorrentI2POutboundLength
        };

        // Downloads
        p.ScanDirectories = new Dictionary<string, SaveLocation>();
        foreach (var (path, action) in r.ScanDirectories)
            p.ScanDirectories[path] = action switch {
                "0" => new SaveLocation(StandardSaveLocation.MonitoredFolder),
                "1" => new SaveLocation(StandardSaveLocation.Default),
                _ => new SaveLocation(action)
            };

        return p;
    }
}
