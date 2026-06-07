using Google.Protobuf.WellKnownTypes;

using Newtonsoft.Json.Linq;

using System.Linq;

using QBittorrent.Client;

using RTSharp.Daemon.Protocols.DataProvider.Settings;

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

    private static JToken? GetAdditional(IDictionary<string, JToken>? data, string key) =>
        data != null && data.TryGetValue(key, out var val) ? val : null;

    private static QBittorrentSettings MapSettingsToProto(Preferences p)
    {
        // Behaviour
        var logEnabled = GetAdditional(p.AdditionalData, "file_log_enabled");
        var logPath = GetAdditional(p.AdditionalData, "file_log_path");
        var logBackupEnabled = GetAdditional(p.AdditionalData, "file_log_backup_enabled");
        var logMaxSize = GetAdditional(p.AdditionalData, "file_log_max_size");
        var logDeleteOld = GetAdditional(p.AdditionalData, "file_log_delete_old");
        var logAge = GetAdditional(p.AdditionalData, "file_log_age");
        var logAgeType = GetAdditional(p.AdditionalData, "file_log_age_type");

        // Downloads
        var torrentContentLayout = GetAdditional(p.AdditionalData, "torrent_content_layout");
        var addToTopOfQueue = GetAdditional(p.AdditionalData, "add_to_top_of_queue");
        var torrentStopCondition = GetAdditional(p.AdditionalData, "torrent_stop_condition");
        var mergeTrackers = GetAdditional(p.AdditionalData, "merge_trackers");
        var autoDeleteMode = GetAdditional(p.AdditionalData, "auto_delete_mode");
        var useUnwantedFolder = GetAdditional(p.AdditionalData, "use_unwanted_folder");
        var useCategoryPathsInManualMode = GetAdditional(p.AdditionalData, "use_category_paths_in_manual_mode");
        var useSubcategories = GetAdditional(p.AdditionalData, "use_subcategories");
        var excludedFileNamesEnabled = GetAdditional(p.AdditionalData, "excluded_file_names_enabled");
        var excludedFileNames = GetAdditional(p.AdditionalData, "excluded_file_names");
        var autorunOnTorrentAddedEnabled = GetAdditional(p.AdditionalData, "autorun_on_torrent_added_enabled");
        var autorunOnTorrentAddedProgram = GetAdditional(p.AdditionalData, "autorun_on_torrent_added_program");

        // BitTorrent
        var trackerListEnabled = GetAdditional(p.AdditionalData, "add_trackers_from_url_enabled");
        var trackerListUrl = GetAdditional(p.AdditionalData, "add_trackers_url");
        var trackerListContent = GetAdditional(p.AdditionalData, "add_trackers_url_list");

        // RSS
        var rssFetchDelay = GetAdditional(p.AdditionalData, "rss_fetch_delay");

        // WebUI
        var reverseProxyEnabled = GetAdditional(p.AdditionalData, "web_ui_reverse_proxy_enabled");
        var reverseProxiesList = GetAdditional(p.AdditionalData, "web_ui_reverse_proxies_list");

        // Advanced
        var resumeDataStorageType = GetAdditional(p.AdditionalData, "resume_data_storage_type");
        var torrentContentRemoveOption = GetAdditional(p.AdditionalData, "torrent_content_remove_option");
        var memoryWorkingSetLimit = GetAdditional(p.AdditionalData, "memory_working_set_limit");
        var saveStatisticsInterval = GetAdditional(p.AdditionalData, "save_statistics_interval");
        var torrentFileSizeLimit = GetAdditional(p.AdditionalData, "torrent_file_size_limit");
        var confirmTorrentRecheck = GetAdditional(p.AdditionalData, "confirm_torrent_recheck");
        var appInstanceName = GetAdditional(p.AdditionalData, "app_instance_name");
        var refreshInterval = GetAdditional(p.AdditionalData, "refresh_interval");
        var reannounceWhenAddressChanged = GetAdditional(p.AdditionalData, "reannounce_when_address_changed");
        var embeddedTrackerPortForwarding = GetAdditional(p.AdditionalData, "embedded_tracker_port_forwarding");
        var ignoreSslErrors = GetAdditional(p.AdditionalData, "ignore_ssl_errors");
        var pythonExecutablePath = GetAdditional(p.AdditionalData, "python_executable_path");
        var diskIOReadMode = GetAdditional(p.AdditionalData, "disk_io_read_mode");
        var diskIOWriteMode = GetAdditional(p.AdditionalData, "disk_io_write_mode");
        var hashingThreads = GetAdditional(p.AdditionalData, "hashing_threads");
        var diskQueueSize = GetAdditional(p.AdditionalData, "disk_queue_size");
        var bdecodeDepthLimit = GetAdditional(p.AdditionalData, "bdecode_depth_limit");
        var bdecodeTokenLimit = GetAdditional(p.AdditionalData, "bdecode_token_limit");
        var socketSendBufferSize = GetAdditional(p.AdditionalData, "socket_send_buffer_size");
        var socketReceiveBufferSize = GetAdditional(p.AdditionalData, "socket_receive_buffer_size");
        var outgoingConnectionsPerSecond = GetAdditional(p.AdditionalData, "connection_speed");
        var upnpLeaseDuration = GetAdditional(p.AdditionalData, "upnp_lease_duration");
        var peerTos = GetAdditional(p.AdditionalData, "peer_tos");
        var idnSupportEnabled = GetAdditional(p.AdditionalData, "idn_support_enabled");
        var validateHttpsTrackerCertificate = GetAdditional(p.AdditionalData, "validate_https_tracker_certificate");
        var ssrfMitigation = GetAdditional(p.AdditionalData, "ssrf_mitigation");
        var blockPeersOnPrivilegedPorts = GetAdditional(p.AdditionalData, "block_peers_on_privileged_ports");
        var announcePort = GetAdditional(p.AdditionalData, "announce_port");
        var peerTurnover = GetAdditional(p.AdditionalData, "peer_turnover");
        var peerTurnoverCutoff = GetAdditional(p.AdditionalData, "peer_turnover_cutoff");
        var peerTurnoverInterval = GetAdditional(p.AdditionalData, "peer_turnover_interval");
        var requestQueueSize = GetAdditional(p.AdditionalData, "request_queue_size");
        var dhtBootstrapNodes = GetAdditional(p.AdditionalData, "dht_bootstrap_nodes");
        var i2pInboundQuantity = GetAdditional(p.AdditionalData, "i2p_inbound_quantity");
        var i2pOutboundQuantity = GetAdditional(p.AdditionalData, "i2p_outbound_quantity");
        var i2pInboundLength = GetAdditional(p.AdditionalData, "i2p_inbound_length");
        var i2pOutboundLength = GetAdditional(p.AdditionalData, "i2p_outbound_length");

        var r = new QBittorrentSettings {
            // Behaviour
            LogEnabled = logEnabled?.Value<bool>(),
            LogPath = logPath?.Value<string>(),
            LogBackupEnabled = logBackupEnabled?.Value<bool>(),
            LogMaxSize = logMaxSize?.Value<int>(),
            LogDeleteOld = logDeleteOld?.Value<bool>(),
            LogAge = logAge?.Value<int>(),
            LogAgeType = logAgeType?.Value<int>(),

            // Downloads
            TorrentContentLayout = torrentContentLayout?.Value<string>(),
            AddToTopOfQueue = addToTopOfQueue?.Value<bool>(),
            AddTorrentPaused = p.AddTorrentPaused,
            TorrentStopCondition = torrentStopCondition?.Value<string>(),
            MergeTrackers = mergeTrackers?.Value<bool>(),
            AutoDeleteMode = autoDeleteMode?.Value<int>(),
            PreallocateAll = p.PreallocateAll,
            AppendExtensionToIncompleteFiles = p.AppendExtensionToIncompleteFiles,
            UseUnwantedFolder = useUnwantedFolder?.Value<bool>(),
            AutoTMMEnabledByDefault = p.AutoTMMEnabledByDefault,
            AutoTMMRetainedWhenCategoryChanges = p.AutoTMMRetainedWhenCategoryChanges,
            AutoTMMRetainedWhenDefaultSavePathChanges = p.AutoTMMRetainedWhenDefaultSavePathChanges,
            AutoTMMRetainedWhenCategorySavePathChanges = p.AutoTMMRetainedWhenCategorySavePathChanges,
            UseSubcategories = useSubcategories?.Value<bool>(),
            UseCategoryPathsInManualMode = useCategoryPathsInManualMode?.Value<bool>(),
            SavePath = p.SavePath,
            TempPathEnabled = p.TempPathEnabled,
            TempPath = p.TempPath,
            ExportDirectory = p.ExportDirectory,
            ExportDirectoryForFinished = p.ExportDirectoryForFinished,
            ExcludedFileNamesEnabled = excludedFileNamesEnabled?.Value<bool>(),
            ExcludedFileNames = excludedFileNames?.Value<string>(),
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
            AutorunOnTorrentAddedEnabled = autorunOnTorrentAddedEnabled?.Value<bool>(),
            AutorunOnTorrentAddedProgram = autorunOnTorrentAddedProgram?.Value<string>(),

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
            ProxyAuthenticationEnabled = p.ProxyAuthenticationEnabled,
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
            TrackerListEnabled = trackerListEnabled?.Value<bool>(),
            TrackerListUrl = trackerListUrl?.Value<string>(),
            TrackerListContent = trackerListContent?.Value<string>(),

            // RSS
            RssProcessingEnabled = p.RssProcessingEnabled,
            RssRefreshInterval = p.RssRefreshInterval,
            RssMaxArticlesPerFeed = p.RssMaxArticlesPerFeed,
            RssAutoDownloadingEnabled = p.RssAutoDownloadingEnabled,
            RssDownloadRepackProperEpisodes = p.RssDownloadRepackProperEpisodes,
            RssFetchDelay = rssFetchDelay?.Value<int>(),

            // WebUI
            WebUIAddress = p.WebUIAddress,
            WebUIPort = p.WebUIPort,
            WebUIUpnp = p.WebUIUpnp,
            WebUIUsername = p.WebUIUsername,
            WebUIPasswordHash = p.WebUIPasswordHash,
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
            ReverseProxyEnabled = reverseProxyEnabled?.Value<bool>(),
            ReverseProxiesList = reverseProxiesList?.Value<string>(),
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
            ResumeDataStorageType = resumeDataStorageType?.Value<string>(),
            TorrentContentRemoveOption = torrentContentRemoveOption?.Value<string>(),
            CurrentNetworkInterface = p.CurrentNetworkInterface,
            CurrentInterfaceAddress = p.CurrentInterfaceAddress,
            SaveResumeDataInterval = p.SaveResumeDataInterval,
            RecheckCompletedTorrents = p.RecheckCompletedTorrents,
            ResolvePeerCountries = p.ResolvePeerCountries,
            MemoryWorkingSetLimit = memoryWorkingSetLimit?.Value<int>(),
            SaveStatisticsInterval = saveStatisticsInterval?.Value<int>(),
            TorrentFileSizeLimit = torrentFileSizeLimit?.Value<long>() is long tfsz ? (int)(tfsz / (1024 * 1024)) : (int?)null,
            ConfirmTorrentRecheck = confirmTorrentRecheck?.Value<bool>(),
            AppInstanceName = appInstanceName?.Value<string>(),
            RefreshInterval = refreshInterval?.Value<int>(),
            ReannounceWhenAddressChanged = reannounceWhenAddressChanged?.Value<bool>(),
            EmbeddedTrackerPortForwarding = embeddedTrackerPortForwarding?.Value<bool>(),
            IgnoreSslErrors = ignoreSslErrors?.Value<bool>(),
            PythonExecutablePath = pythonExecutablePath?.Value<string>(),
            LibtorrentAsynchronousIOThreads = p.LibtorrentAsynchronousIOThreads,
            LibtorrentFilePoolSize = p.LibtorrentFilePoolSize,
            LibtorrentOutstandingMemoryWhenCheckingTorrent = p.LibtorrentOutstandingMemoryWhenCheckingTorrent,
            LibtorrentDiskCache = p.LibtorrentDiskCache,
            LibtorrentDiskCacheExpiryInterval = p.LibtorrentDiskCacheExpiryInterval,
            LibtorrentDiskIOReadMode = diskIOReadMode?.Value<int>(),
            LibtorrentDiskIOWriteMode = diskIOWriteMode?.Value<int>(),
            LibtorrentHashingThreads = hashingThreads?.Value<int>(),
            LibtorrentDiskQueueSize = diskQueueSize?.Value<long>() is long dqs ? (int)(dqs / 1024) : (int?)null,
            LibtorrentBdecodeDepthLimit = bdecodeDepthLimit?.Value<int>(),
            LibtorrentBdecodeTokenLimit = bdecodeTokenLimit?.Value<int>(),
            LibtorrentCoalesceReadsAndWrites = p.LibtorrentCoalesceReadsAndWrites,
            LibtorrentPieceExtentAffinity = p.LibtorrentPieceExtentAffinity,
            LibtorrentSendUploadPieceSuggestions = p.LibtorrentSendUploadPieceSuggestions,
            LibtorrentSendBufferWatermark = p.LibtorrentSendBufferWatermark,
            LibtorrentSendBufferLowWatermark = p.LibtorrentSendBufferLowWatermark,
            LibtorrentSendBufferWatermarkFactor = p.LibtorrentSendBufferWatermarkFactor,
            LibtorrentSocketBacklogSize = p.LibtorrentSocketBacklogSize,
            LibtorrentSocketSendBufferSize = socketSendBufferSize?.Value<int>(),
            LibtorrentSocketReceiveBufferSize = socketReceiveBufferSize?.Value<int>(),
            LibtorrentOutgoingPortsMin = p.LibtorrentOutgoingPortsMin,
            LibtorrentOutgoingPortsMax = p.LibtorrentOutgoingPortsMax,
            LibtorrentOutgoingConnectionsPerSecond = outgoingConnectionsPerSecond?.Value<int>(),
            LibtorrentUpnpLeaseDuration = upnpLeaseDuration?.Value<int>(),
            LibtorrentPeerTos = peerTos?.Value<int>(),
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
            LibtorrentIdnSupportEnabled = idnSupportEnabled?.Value<bool>(),
            LibtorrentValidateHttpsTrackerCertificate = validateHttpsTrackerCertificate?.Value<bool>(),
            LibtorrentSsrfMitigation = ssrfMitigation?.Value<bool>(),
            LibtorrentBlockPeersOnPrivilegedPorts = blockPeersOnPrivilegedPorts?.Value<bool>(),
            LibtorrentAnnouncePort = announcePort?.Value<int>(),
            LibtorrentPeerTurnover = peerTurnover?.Value<int>(),
            LibtorrentPeerTurnoverCutoff = peerTurnoverCutoff?.Value<int>(),
            LibtorrentPeerTurnoverInterval = peerTurnoverInterval?.Value<int>(),
            LibtorrentRequestQueueSize = requestQueueSize?.Value<int>(),
            LibtorrentDhtBootstrapNodes = dhtBootstrapNodes?.Value<string>(),
            LibtorrentI2PInboundQuantity = i2pInboundQuantity?.Value<int>(),
            LibtorrentI2POutboundQuantity = i2pOutboundQuantity?.Value<int>(),
            LibtorrentI2PInboundLength = i2pInboundLength?.Value<int>(),
            LibtorrentI2POutboundLength = i2pOutboundLength?.Value<int>()
        };

        // Downloads
        if (p.ScanDirectories != null)
            foreach (var (path, loc) in p.ScanDirectories)
                r.ScanDirectories[path] = loc.CustomFolder != null ? loc.CustomFolder
                    : loc.StandardFolder == StandardSaveLocation.MonitoredFolder ? "0" : "1";
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
            // Downloads
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
            ProxyAuthenticationEnabled = r.ProxyAuthenticationEnabled,
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

            // RSS
            RssProcessingEnabled = r.RssProcessingEnabled,
            RssRefreshInterval = r.RssRefreshInterval,
            RssMaxArticlesPerFeed = r.RssMaxArticlesPerFeed,
            RssAutoDownloadingEnabled = r.RssAutoDownloadingEnabled,
            RssDownloadRepackProperEpisodes = r.RssDownloadRepackProperEpisodes,
            RssSmartEpisodeFilters = r.RssSmartEpisodeFilters.ToList(),

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
            LibtorrentMaxConcurrentHttpAnnounces = r.LibtorrentMaxConcurrentHttpAnnounces
        };

        p.AdditionalData ??= new Dictionary<string, JToken>();

        // Behaviour
        if (r.LogEnabled != null) p.AdditionalData["file_log_enabled"] = r.LogEnabled.Value;
        if (r.LogPath != null) p.AdditionalData["file_log_path"] = r.LogPath;
        if (r.LogBackupEnabled != null) p.AdditionalData["file_log_backup_enabled"] = r.LogBackupEnabled.Value;
        if (r.LogMaxSize != null) p.AdditionalData["file_log_max_size"] = r.LogMaxSize.Value;
        if (r.LogDeleteOld != null) p.AdditionalData["file_log_delete_old"] = r.LogDeleteOld.Value;
        if (r.LogAge != null) p.AdditionalData["file_log_age"] = r.LogAge.Value;
        if (r.LogAgeType != null) p.AdditionalData["file_log_age_type"] = r.LogAgeType.Value;

        // Downloads
        if (r.TorrentContentLayout != null) p.AdditionalData["torrent_content_layout"] = r.TorrentContentLayout;
        if (r.AddToTopOfQueue != null) p.AdditionalData["add_to_top_of_queue"] = r.AddToTopOfQueue.Value;
        if (r.TorrentStopCondition != null) p.AdditionalData["torrent_stop_condition"] = r.TorrentStopCondition;
        if (r.MergeTrackers != null) p.AdditionalData["merge_trackers"] = r.MergeTrackers.Value;
        if (r.AutoDeleteMode != null) p.AdditionalData["auto_delete_mode"] = r.AutoDeleteMode.Value;
        if (r.UseUnwantedFolder != null) p.AdditionalData["use_unwanted_folder"] = r.UseUnwantedFolder.Value;
        if (r.UseCategoryPathsInManualMode != null) p.AdditionalData["use_category_paths_in_manual_mode"] = r.UseCategoryPathsInManualMode.Value;
        if (r.UseSubcategories != null) p.AdditionalData["use_subcategories"] = r.UseSubcategories.Value;
        if (r.ExcludedFileNamesEnabled != null) p.AdditionalData["excluded_file_names_enabled"] = r.ExcludedFileNamesEnabled.Value;
        if (r.ExcludedFileNames != null) p.AdditionalData["excluded_file_names"] = r.ExcludedFileNames;
        p.ScanDirectories = new Dictionary<string, SaveLocation>();
        foreach (var (path, action) in r.ScanDirectories)
            p.ScanDirectories[path] = action switch {
                "0" => new SaveLocation(StandardSaveLocation.MonitoredFolder),
                "1" => new SaveLocation(StandardSaveLocation.Default),
                _ => new SaveLocation(action)
            };
        if (r.AutorunOnTorrentAddedEnabled != null) p.AdditionalData["autorun_on_torrent_added_enabled"] = r.AutorunOnTorrentAddedEnabled.Value;
        if (r.AutorunOnTorrentAddedProgram != null) p.AdditionalData["autorun_on_torrent_added_program"] = r.AutorunOnTorrentAddedProgram;

        // BitTorrent
        if (r.TrackerListEnabled != null) p.AdditionalData["add_trackers_from_url_enabled"] = r.TrackerListEnabled.Value;
        if (r.TrackerListUrl != null) p.AdditionalData["add_trackers_url"] = r.TrackerListUrl;

        // RSS
        if (r.RssFetchDelay != null) p.AdditionalData["rss_fetch_delay"] = r.RssFetchDelay.Value;

        // WebUI
        if (r.ReverseProxyEnabled != null) p.AdditionalData["web_ui_reverse_proxy_enabled"] = r.ReverseProxyEnabled.Value;
        if (r.ReverseProxiesList != null) p.AdditionalData["web_ui_reverse_proxies_list"] = r.ReverseProxiesList;

        // Advanced
        if (r.ResumeDataStorageType != null) p.AdditionalData["resume_data_storage_type"] = r.ResumeDataStorageType;
        if (r.TorrentContentRemoveOption != null) p.AdditionalData["torrent_content_remove_option"] = r.TorrentContentRemoveOption;
        if (r.MemoryWorkingSetLimit != null) p.AdditionalData["memory_working_set_limit"] = r.MemoryWorkingSetLimit.Value;
        if (r.SaveStatisticsInterval != null) p.AdditionalData["save_statistics_interval"] = r.SaveStatisticsInterval.Value;
        if (r.TorrentFileSizeLimit != null) p.AdditionalData["torrent_file_size_limit"] = (long)r.TorrentFileSizeLimit.Value * 1024 * 1024;
        if (r.ConfirmTorrentRecheck != null) p.AdditionalData["confirm_torrent_recheck"] = r.ConfirmTorrentRecheck.Value;
        if (r.AppInstanceName != null) p.AdditionalData["app_instance_name"] = r.AppInstanceName;
        if (r.RefreshInterval != null) p.AdditionalData["refresh_interval"] = r.RefreshInterval.Value;
        if (r.ReannounceWhenAddressChanged != null) p.AdditionalData["reannounce_when_address_changed"] = r.ReannounceWhenAddressChanged.Value;
        if (r.EmbeddedTrackerPortForwarding != null) p.AdditionalData["embedded_tracker_port_forwarding"] = r.EmbeddedTrackerPortForwarding.Value;
        if (r.IgnoreSslErrors != null) p.AdditionalData["ignore_ssl_errors"] = r.IgnoreSslErrors.Value;
        if (r.PythonExecutablePath != null) p.AdditionalData["python_executable_path"] = r.PythonExecutablePath;
        if (r.LibtorrentDiskIOReadMode != null) p.AdditionalData["disk_io_read_mode"] = r.LibtorrentDiskIOReadMode.Value;
        if (r.LibtorrentDiskIOWriteMode != null) p.AdditionalData["disk_io_write_mode"] = r.LibtorrentDiskIOWriteMode.Value;
        if (r.LibtorrentHashingThreads != null) p.AdditionalData["hashing_threads"] = r.LibtorrentHashingThreads.Value;
        if (r.LibtorrentDiskQueueSize != null) p.AdditionalData["disk_queue_size"] = (long)r.LibtorrentDiskQueueSize.Value * 1024;
        if (r.LibtorrentBdecodeDepthLimit != null) p.AdditionalData["bdecode_depth_limit"] = r.LibtorrentBdecodeDepthLimit.Value;
        if (r.LibtorrentBdecodeTokenLimit != null) p.AdditionalData["bdecode_token_limit"] = r.LibtorrentBdecodeTokenLimit.Value;
        if (r.LibtorrentSocketSendBufferSize != null) p.AdditionalData["socket_send_buffer_size"] = r.LibtorrentSocketSendBufferSize.Value;
        if (r.LibtorrentSocketReceiveBufferSize != null) p.AdditionalData["socket_receive_buffer_size"] = r.LibtorrentSocketReceiveBufferSize.Value;
        if (r.LibtorrentOutgoingConnectionsPerSecond != null) p.AdditionalData["connection_speed"] = r.LibtorrentOutgoingConnectionsPerSecond.Value;
        if (r.LibtorrentUpnpLeaseDuration != null) p.AdditionalData["upnp_lease_duration"] = r.LibtorrentUpnpLeaseDuration.Value;
        if (r.LibtorrentPeerTos != null) p.AdditionalData["peer_tos"] = r.LibtorrentPeerTos.Value;
        if (r.LibtorrentIdnSupportEnabled != null) p.AdditionalData["idn_support_enabled"] = r.LibtorrentIdnSupportEnabled.Value;
        if (r.LibtorrentValidateHttpsTrackerCertificate != null) p.AdditionalData["validate_https_tracker_certificate"] = r.LibtorrentValidateHttpsTrackerCertificate.Value;
        if (r.LibtorrentSsrfMitigation != null) p.AdditionalData["ssrf_mitigation"] = r.LibtorrentSsrfMitigation.Value;
        if (r.LibtorrentBlockPeersOnPrivilegedPorts != null) p.AdditionalData["block_peers_on_privileged_ports"] = r.LibtorrentBlockPeersOnPrivilegedPorts.Value;
        if (r.LibtorrentAnnouncePort != null) p.AdditionalData["announce_port"] = r.LibtorrentAnnouncePort.Value;
        if (r.LibtorrentPeerTurnover != null) p.AdditionalData["peer_turnover"] = r.LibtorrentPeerTurnover.Value;
        if (r.LibtorrentPeerTurnoverCutoff != null) p.AdditionalData["peer_turnover_cutoff"] = r.LibtorrentPeerTurnoverCutoff.Value;
        if (r.LibtorrentPeerTurnoverInterval != null) p.AdditionalData["peer_turnover_interval"] = r.LibtorrentPeerTurnoverInterval.Value;
        if (r.LibtorrentRequestQueueSize != null) p.AdditionalData["request_queue_size"] = r.LibtorrentRequestQueueSize.Value;
        if (r.LibtorrentDhtBootstrapNodes != null) p.AdditionalData["dht_bootstrap_nodes"] = r.LibtorrentDhtBootstrapNodes;
        if (r.LibtorrentI2PInboundQuantity != null) p.AdditionalData["i2p_inbound_quantity"] = r.LibtorrentI2PInboundQuantity.Value;
        if (r.LibtorrentI2POutboundQuantity != null) p.AdditionalData["i2p_outbound_quantity"] = r.LibtorrentI2POutboundQuantity.Value;
        if (r.LibtorrentI2PInboundLength != null) p.AdditionalData["i2p_inbound_length"] = r.LibtorrentI2PInboundLength.Value;
        if (r.LibtorrentI2POutboundLength != null) p.AdditionalData["i2p_outbound_length"] = r.LibtorrentI2POutboundLength.Value;

        return p;
    }
}
