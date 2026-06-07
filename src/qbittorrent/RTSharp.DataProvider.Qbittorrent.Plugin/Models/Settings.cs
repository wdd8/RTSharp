using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using RTSharp.DataProvider.Qbittorrent.Plugin.Mappers;

namespace RTSharp.DataProvider.Qbittorrent.Plugin.Models
{
    public partial class ScanDirectory : ObservableObject
    {
        [ObservableProperty]
        public partial string Path { get; set; } = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActionComboSelection))]
        [NotifyPropertyChangedFor(nameof(IsCustomPath))]
        public partial string Action { get; set; } = SettingsMapper.ScanDirectoryActionOptions[0];

        public bool IsCustomPath => !SettingsMapper.ScanDirectoryActionOptions.Contains(Action);

        public string ActionComboSelection {
            get => SettingsMapper.ScanDirectoryActionOptions.Contains(Action) ? Action : "Other...";
            set {
                if (value == "Other...") {
                    if (SettingsMapper.ScanDirectoryActionOptions.Contains(Action))
                        Action = "";
                } else {
                    Action = value ?? "";
                }
                OnPropertyChanged();
            }
        }
    }

    public partial class Downloads : ObservableObject
    {
        [ObservableProperty] public partial string? SavePath { get; set; }
        [ObservableProperty] public partial bool TempPathEnabled { get; set; }
        [ObservableProperty] public partial string? TempPath { get; set; }
        [ObservableProperty] public partial string? ExportDirectory { get; set; }
        [ObservableProperty] public partial string? ExportDirectoryForFinished { get; set; }
        [ObservableProperty] public partial bool PreallocateAll { get; set; }
        [ObservableProperty] public partial bool AppendExtensionToIncompleteFiles { get; set; }
        [ObservableProperty] public partial string? AutoTMMEnabledByDefault { get; set; }
        [ObservableProperty] public partial string? AutoTMMRetainedWhenCategoryChanges { get; set; }
        [ObservableProperty] public partial string? AutoTMMRetainedWhenDefaultSavePathChanges { get; set; }
        [ObservableProperty] public partial string? AutoTMMRetainedWhenCategorySavePathChanges { get; set; }
        [ObservableProperty] public partial bool UseSubcategories { get; set; }
        [ObservableProperty] public partial bool UseCategoryPathsInManualMode { get; set; }
        [ObservableProperty] public partial bool MailNotificationEnabled { get; set; }
        [ObservableProperty] public partial string? MailNotificationSender { get; set; }
        [ObservableProperty] public partial string? MailNotificationEmailAddress { get; set; }
        [ObservableProperty] public partial string? MailNotificationSmtpServer { get; set; }
        [ObservableProperty] public partial bool MailNotificationSslEnabled { get; set; }
        [ObservableProperty] public partial bool MailNotificationAuthenticationEnabled { get; set; }
        [ObservableProperty] public partial string? MailNotificationUsername { get; set; }
        [ObservableProperty] public partial string? MailNotificationPassword { get; set; }
        [ObservableProperty] public partial string? TorrentContentLayout { get; set; }
        [ObservableProperty] public partial bool AddToTopOfQueue { get; set; }
        [ObservableProperty] public partial bool AddTorrentPaused { get; set; }
        [ObservableProperty] public partial string? TorrentStopCondition { get; set; }
        [ObservableProperty] public partial bool MergeTrackers { get; set; }
        [ObservableProperty] public partial bool AutoDeleteEnabled { get; set; }
        [ObservableProperty] public partial bool AutoDeleteAlsoWhenCancelled { get; set; }
        [ObservableProperty] public partial bool UseUnwantedFolder { get; set; }
        public ObservableCollection<ScanDirectory> ScanDirectories { get; set; } = new();
        [ObservableProperty] public partial bool ExcludedFileNamesEnabled { get; set; }
        [ObservableProperty] public partial string? ExcludedFileNames { get; set; }
        [ObservableProperty] public partial bool AutorunOnTorrentAddedEnabled { get; set; }
        [ObservableProperty] public partial string? AutorunOnTorrentAddedProgram { get; set; }
        [ObservableProperty] public partial bool AutorunEnabled { get; set; }
        [ObservableProperty] public partial string? AutorunProgram { get; set; }
    }

    public partial class Connection : ObservableObject
    {
        [ObservableProperty] public partial string? BittorrentProtocol { get; set; }
        [ObservableProperty] public partial int? ListenPort { get; set; }
        [ObservableProperty] public partial bool UpnpEnabled { get; set; }
        [ObservableProperty] public partial bool MaxConnectionsEnabled { get; set; }
        [ObservableProperty] public partial int? MaxConnections { get; set; }
        [ObservableProperty] public partial bool MaxConnectionsPerTorrentEnabled { get; set; }
        [ObservableProperty] public partial int? MaxConnectionsPerTorrent { get; set; }
        [ObservableProperty] public partial bool MaxUploadsEnabled { get; set; }
        [ObservableProperty] public partial int? MaxUploads { get; set; }
        [ObservableProperty] public partial bool MaxUploadsPerTorrentEnabled { get; set; }
        [ObservableProperty] public partial int? MaxUploadsPerTorrent { get; set; }

        partial void OnMaxConnectionsEnabledChanged(bool value)
        {
            if (value && (MaxConnections == null || MaxConnections <= 0))
                MaxConnections = 500;
        }

        partial void OnMaxConnectionsPerTorrentEnabledChanged(bool value)
        {
            if (value && (MaxConnectionsPerTorrent == null || MaxConnectionsPerTorrent <= 0))
                MaxConnectionsPerTorrent = 100;
        }

        partial void OnMaxUploadsEnabledChanged(bool value)
        {
            if (value && (MaxUploads == null || MaxUploads <= 0))
                MaxUploads = 500;
        }

        partial void OnMaxUploadsPerTorrentEnabledChanged(bool value)
        {
            if (value && (MaxUploadsPerTorrent == null || MaxUploadsPerTorrent <= 0))
                MaxUploadsPerTorrent = 100;
        }
        [ObservableProperty] public partial string? ProxyType { get; set; }
        [ObservableProperty] public partial string? ProxyAddress { get; set; }
        [ObservableProperty] public partial int? ProxyPort { get; set; }
        [ObservableProperty] public partial bool ProxyHostnameLookup { get; set; }
        [ObservableProperty] public partial bool ProxyAuthenticationEnabled { get; set; }
        [ObservableProperty] public partial string? ProxyUsername { get; set; }
        [ObservableProperty] public partial string? ProxyPassword { get; set; }
        [ObservableProperty] public partial bool ProxyBittorrent { get; set; }
        [ObservableProperty] public partial bool ProxyPeerConnections { get; set; }
        [ObservableProperty] public partial bool ProxyRss { get; set; }
        [ObservableProperty] public partial bool ProxyMisc { get; set; }
        [ObservableProperty] public partial bool IpFilterEnabled { get; set; }
        [ObservableProperty] public partial string? IpFilterPath { get; set; }
        [ObservableProperty] public partial bool IpFilterTrackers { get; set; }
        [ObservableProperty] public partial string? BannedIpAddresses { get; set; }
    }

    public partial class Speed : ObservableObject
    {
        [ObservableProperty] public partial int? DownloadLimit { get; set; }
        [ObservableProperty] public partial int? UploadLimit { get; set; }
        [ObservableProperty] public partial int? AlternativeDownloadLimit { get; set; }
        [ObservableProperty] public partial int? AlternativeUploadLimit { get; set; }
        [ObservableProperty] public partial bool SchedulerEnabled { get; set; }
        [ObservableProperty] public partial int? ScheduleFromHour { get; set; }
        [ObservableProperty] public partial int? ScheduleFromMinute { get; set; }
        [ObservableProperty] public partial int? ScheduleToHour { get; set; }
        [ObservableProperty] public partial int? ScheduleToMinute { get; set; }
        [ObservableProperty] public partial string? SchedulerDays { get; set; }
        [ObservableProperty] public partial bool LimitUTPRate { get; set; }
        [ObservableProperty] public partial bool LimitTcpOverhead { get; set; }
        [ObservableProperty] public partial bool LimitLAN { get; set; }
    }

    public partial class BitTorrent : ObservableObject
    {
        [ObservableProperty] public partial bool DHT { get; set; }
        [ObservableProperty] public partial bool PeerExchange { get; set; }
        [ObservableProperty] public partial bool LocalPeerDiscovery { get; set; }
        [ObservableProperty] public partial string? Encryption { get; set; }
        [ObservableProperty] public partial bool AnonymousMode { get; set; }
        [ObservableProperty] public partial bool QueueingEnabled { get; set; }
        [ObservableProperty] public partial int? MaxActiveDownloads { get; set; }
        [ObservableProperty] public partial int? MaxActiveUploads { get; set; }
        [ObservableProperty] public partial int? MaxActiveTorrents { get; set; }
        [ObservableProperty] public partial bool DoNotCountSlowTorrents { get; set; }
        [ObservableProperty] public partial int? SlowTorrentDownloadRateThreshold { get; set; }
        [ObservableProperty] public partial int? SlowTorrentUploadRateThreshold { get; set; }
        [ObservableProperty] public partial int? SlowTorrentInactiveTime { get; set; }
        [ObservableProperty] public partial bool MaxRatioEnabled { get; set; }
        [ObservableProperty] public partial double? MaxRatio { get; set; }
        [ObservableProperty] public partial bool MaxSeedingTimeEnabled { get; set; }
        [ObservableProperty] public partial int? MaxSeedingTime { get; set; }
        [ObservableProperty] public partial bool MaxInactiveSeedingTimeEnabled { get; set; }
        [ObservableProperty] public partial int? MaxInactiveSeedingTime { get; set; }
        [ObservableProperty] public partial string? MaxRatioAction { get; set; }
        [ObservableProperty] public partial bool AdditionalTrackersEnabled { get; set; }
        [ObservableProperty] public partial string? AdditionalTrackers { get; set; }
        [ObservableProperty] public partial bool TrackerListEnabled { get; set; }
        [ObservableProperty] public partial string? TrackerListUrl { get; set; }
        [ObservableProperty] public partial string? TrackerListContent { get; set; }

        partial void OnMaxRatioEnabledChanged(bool value)
        {
            if (value && (MaxRatio == null || MaxRatio <= 0))
                MaxRatio = 1.0;
        }

        partial void OnMaxSeedingTimeEnabledChanged(bool value)
        {
            if (value && (MaxSeedingTime == null || MaxSeedingTime <= 0))
                MaxSeedingTime = 1440;
        }

        partial void OnMaxInactiveSeedingTimeEnabledChanged(bool value)
        {
            if (value && (MaxInactiveSeedingTime == null || MaxInactiveSeedingTime <= 0))
                MaxInactiveSeedingTime = 1440;
        }
    }

    public partial class Rss : ObservableObject
    {
        [ObservableProperty] public partial bool RssProcessingEnabled { get; set; }
        [ObservableProperty] public partial int? RssRefreshInterval { get; set; }
        [ObservableProperty] public partial int? RssFetchDelay { get; set; }
        [ObservableProperty] public partial int? RssMaxArticlesPerFeed { get; set; }
        [ObservableProperty] public partial bool RssAutoDownloadingEnabled { get; set; }
        [ObservableProperty] public partial bool RssDownloadRepackProperEpisodes { get; set; }
        [ObservableProperty] public partial string? RssSmartEpisodeFilters { get; set; }
    }

    public partial class WebUI : ObservableObject
    {
        [ObservableProperty] public partial string? WebUIAddress { get; set; }
        [ObservableProperty] public partial int? WebUIPort { get; set; }
        [ObservableProperty] public partial bool WebUIUpnp { get; set; }
        [ObservableProperty] public partial string? WebUIUsername { get; set; }
        [ObservableProperty] public partial string? WebUIPassword { get; set; }
        [ObservableProperty] public partial string? WebUIPasswordHash { get; set; }
        [ObservableProperty] public partial bool WebUIHttps { get; set; }
        [ObservableProperty] public partial string? WebUISslKeyPath { get; set; }
        [ObservableProperty] public partial string? WebUISslCertificatePath { get; set; }
        [ObservableProperty] public partial bool WebUIClickjackingProtection { get; set; }
        [ObservableProperty] public partial bool WebUICsrfProtection { get; set; }
        [ObservableProperty] public partial bool WebUISecureCookie { get; set; }
        [ObservableProperty] public partial int? WebUIMaxAuthenticationFailures { get; set; }
        [ObservableProperty] public partial int? WebUIBanDuration { get; set; }
        [ObservableProperty] public partial int? WebUISessionTimeout { get; set; }
        [ObservableProperty] public partial bool AlternativeWebUIEnabled { get; set; }
        [ObservableProperty] public partial string? AlternativeWebUIPath { get; set; }
        [ObservableProperty] public partial bool WebUIHostHeaderValidation { get; set; }
        [ObservableProperty] public partial string? WebUIDomain { get; set; }
        [ObservableProperty] public partial bool WebUICustomHttpHeadersEnabled { get; set; }
        [ObservableProperty] public partial string? WebUICustomHttpHeaders { get; set; }
        [ObservableProperty] public partial bool BypassLocalAuthentication { get; set; }
        [ObservableProperty] public partial bool BypassAuthenticationSubnetWhitelistEnabled { get; set; }
        [ObservableProperty] public partial string? BypassAuthenticationSubnetWhitelist { get; set; }
        [ObservableProperty] public partial bool DynamicDnsEnabled { get; set; }
        [ObservableProperty] public partial string? DynamicDnsService { get; set; }
        [ObservableProperty] public partial string? DynamicDnsUsername { get; set; }
        [ObservableProperty] public partial string? DynamicDnsPassword { get; set; }
        [ObservableProperty] public partial string? DynamicDnsDomain { get; set; }
        [ObservableProperty] public partial bool ReverseProxyEnabled { get; set; }
        [ObservableProperty] public partial string? ReverseProxiesList { get; set; }
    }

    public partial class Advanced : ObservableObject
    {
        [ObservableProperty] public partial string? ResumeDataStorageType { get; set; }
        [ObservableProperty] public partial string? TorrentContentRemoveOption { get; set; }
        [ObservableProperty] public partial int? MemoryWorkingSetLimit { get; set; }
        [ObservableProperty] public partial string? CurrentNetworkInterface { get; set; }
        [ObservableProperty] public partial string? CurrentInterfaceAddress { get; set; }
        [ObservableProperty] public partial int? SaveResumeDataInterval { get; set; }
        [ObservableProperty] public partial int? SaveStatisticsInterval { get; set; }
        [ObservableProperty] public partial int? TorrentFileSizeLimit { get; set; }
        [ObservableProperty] public partial bool ConfirmTorrentRecheck { get; set; }
        [ObservableProperty] public partial bool RecheckCompletedTorrents { get; set; }
        [ObservableProperty] public partial bool ResolvePeerCountries { get; set; }
        [ObservableProperty] public partial string? AppInstanceName { get; set; }
        [ObservableProperty] public partial int? RefreshInterval { get; set; }
        [ObservableProperty] public partial bool ReannounceWhenAddressChanged { get; set; }
        [ObservableProperty] public partial int? LibtorrentAsynchronousIOThreads { get; set; }
        [ObservableProperty] public partial int? LibtorrentFilePoolSize { get; set; }
        [ObservableProperty] public partial int? LibtorrentOutstandingMemoryWhenCheckingTorrent { get; set; }
        [ObservableProperty] public partial int? LibtorrentHashingThreads { get; set; }
        [ObservableProperty] public partial int? LibtorrentDiskCache { get; set; }
        [ObservableProperty] public partial int? LibtorrentDiskCacheExpiryInterval { get; set; }
        [ObservableProperty] public partial string? LibtorrentDiskIOReadMode { get; set; }
        [ObservableProperty] public partial string? LibtorrentDiskIOWriteMode { get; set; }
        [ObservableProperty] public partial int? LibtorrentDiskQueueSize { get; set; }
        [ObservableProperty] public partial int? LibtorrentBdecodeDepthLimit { get; set; }
        [ObservableProperty] public partial int? LibtorrentBdecodeTokenLimit { get; set; }
        [ObservableProperty] public partial bool LibtorrentCoalesceReadsAndWrites { get; set; }
        [ObservableProperty] public partial bool LibtorrentPieceExtentAffinity { get; set; }
        [ObservableProperty] public partial bool LibtorrentSendUploadPieceSuggestions { get; set; }
        [ObservableProperty] public partial int? LibtorrentSendBufferWatermark { get; set; }
        [ObservableProperty] public partial int? LibtorrentSendBufferLowWatermark { get; set; }
        [ObservableProperty] public partial int? LibtorrentSendBufferWatermarkFactor { get; set; }
        [ObservableProperty] public partial int? LibtorrentSocketSendBufferSize { get; set; }
        [ObservableProperty] public partial int? LibtorrentSocketReceiveBufferSize { get; set; }
        [ObservableProperty] public partial int? LibtorrentSocketBacklogSize { get; set; }
        [ObservableProperty] public partial int? LibtorrentOutgoingPortsMin { get; set; }
        [ObservableProperty] public partial int? LibtorrentOutgoingPortsMax { get; set; }
        [ObservableProperty] public partial int? LibtorrentOutgoingConnectionsPerSecond { get; set; }
        [ObservableProperty] public partial int? LibtorrentUpnpLeaseDuration { get; set; }
        [ObservableProperty] public partial int? LibtorrentPeerTos { get; set; }
        [ObservableProperty] public partial string? LibtorrentUtpTcpMixedModeAlgorithm { get; set; }
        [ObservableProperty] public partial bool LibtorrentAllowMultipleConnectionsFromSameIp { get; set; }
        [ObservableProperty] public partial bool LibtorrentEnableEmbeddedTracker { get; set; }
        [ObservableProperty] public partial int? LibtorrentEmbeddedTrackerPort { get; set; }
        [ObservableProperty] public partial bool EmbeddedTrackerPortForwarding { get; set; }
        [ObservableProperty] public partial bool IgnoreSslErrors { get; set; }
        [ObservableProperty] public partial string? PythonExecutablePath { get; set; }
        [ObservableProperty] public partial string? LibtorrentUploadSlotsBehavior { get; set; }
        [ObservableProperty] public partial string? LibtorrentUploadChokingAlgorithm { get; set; }
        [ObservableProperty] public partial bool LibtorrentAnnounceToAllTrackers { get; set; }
        [ObservableProperty] public partial bool LibtorrentAnnounceToAllTiers { get; set; }
        [ObservableProperty] public partial string? LibtorrentAnnounceIp { get; set; }
        [ObservableProperty] public partial int? LibtorrentMaxConcurrentHttpAnnounces { get; set; }
        [ObservableProperty] public partial int? LibtorrentStopTrackerTimeout { get; set; }
        [ObservableProperty] public partial bool LibtorrentValidateHttpsTrackerCertificate { get; set; }
        [ObservableProperty] public partial bool LibtorrentIdnSupportEnabled { get; set; }
        [ObservableProperty] public partial bool LibtorrentSsrfMitigation { get; set; }
        [ObservableProperty] public partial bool LibtorrentBlockPeersOnPrivilegedPorts { get; set; }
        [ObservableProperty] public partial int? LibtorrentAnnouncePort { get; set; }
        [ObservableProperty] public partial int? LibtorrentPeerTurnover { get; set; }
        [ObservableProperty] public partial int? LibtorrentPeerTurnoverCutoff { get; set; }
        [ObservableProperty] public partial int? LibtorrentPeerTurnoverInterval { get; set; }
        [ObservableProperty] public partial int? LibtorrentRequestQueueSize { get; set; }
        [ObservableProperty] public partial string? LibtorrentDhtBootstrapNodes { get; set; }
        [ObservableProperty] public partial int? LibtorrentI2pInboundQuantity { get; set; }
        [ObservableProperty] public partial int? LibtorrentI2pOutboundQuantity { get; set; }
        [ObservableProperty] public partial int? LibtorrentI2pInboundLength { get; set; }
        [ObservableProperty] public partial int? LibtorrentI2pOutboundLength { get; set; }
    }

    public partial class Behaviour : ObservableObject
    {
        [ObservableProperty] public partial bool LogEnabled { get; set; }
        [ObservableProperty] public partial string? LogPath { get; set; }
        [ObservableProperty] public partial bool LogBackupEnabled { get; set; }
        [ObservableProperty] public partial int? LogMaxSize { get; set; }
        [ObservableProperty] public partial bool LogDeleteOld { get; set; }
        [ObservableProperty] public partial int? LogAge { get; set; }
        [ObservableProperty] public partial string? LogAgeType { get; set; }
    }

    public partial class Settings : ObservableObject
    {
        public Behaviour Behaviour { get; set; } = new Behaviour();
        public Downloads Downloads { get; set; } = new Downloads();
        public Connection Connection { get; set; } = new Connection();
        public Speed Speed { get; set; } = new Speed();
        public BitTorrent BitTorrent { get; set; } = new BitTorrent();
        public Rss Rss { get; set; } = new Rss();
        public WebUI WebUI { get; set; } = new WebUI();
        public Advanced Advanced { get; set; } = new Advanced();
    }
}
