using RTSharp.Daemon.Protocols.DataProvider.Settings;

namespace RTSharp.DataProvider.Rtorrent.Plugin.Mappers;

public static class SettingsMapper
{
    private record AdvancedSetting(string Property, Func<RtorrentSettings, string> Get, Action<RtorrentSettings, string> Set);

    private static AdvancedSetting Str(string Property, Func<RtorrentSettings, string> Get, Action<RtorrentSettings, string> Set) =>
        new(Property, Get, Set);

    private static AdvancedSetting Num(string Property, Func<RtorrentSettings, long> Get, Action<RtorrentSettings, long> Set) =>
        new(Property, x => Get(x).ToString(), (x, v) => Set(x, Int64.Parse(v)));

    private static AdvancedSetting Bool(string Property, Func<RtorrentSettings, bool> Get, Action<RtorrentSettings, bool> Set) =>
        new(Property, x => Get(x).ToString(), (x, v) => Set(x, Boolean.Parse(v)));

    private static readonly Dictionary<string, AdvancedSetting> AdvancedSettings = new() {
        ["network_http_cacert"] = Str(nameof(RtorrentSettings.NetworkHttpCacert), x => x.NetworkHttpCacert, (x, v) => x.NetworkHttpCacert = v),
        ["network_http_capath"] = Str(nameof(RtorrentSettings.NetworkHttpCapath), x => x.NetworkHttpCapath, (x, v) => x.NetworkHttpCapath = v),
        ["throttle_max_downloads_div"] = Num(nameof(RtorrentSettings.ThrottleMaxDownloadsDiv), x => x.ThrottleMaxDownloadsDiv, (x, v) => x.ThrottleMaxDownloadsDiv = v),
        ["throttle_max_uploads_div"] = Num(nameof(RtorrentSettings.ThrottleMaxUploadsDiv), x => x.ThrottleMaxUploadsDiv, (x, v) => x.ThrottleMaxUploadsDiv = v),
        ["system_file_max_size"] = Num(nameof(RtorrentSettings.SystemFileMaxSize), x => x.SystemFileMaxSize, (x, v) => x.SystemFileMaxSize = v),
        ["pieces_preload_type"] = Str(nameof(RtorrentSettings.PiecesPreloadType), x => x.PiecesPreloadType, (x, v) => x.PiecesPreloadType = v),
        ["pieces_preload_min_size"] = Num(nameof(RtorrentSettings.PiecesPreloadMinSize), x => x.PiecesPreloadMinSize, (x, v) => x.PiecesPreloadMinSize = v),
        ["pieces_preload_min_rate"] = Num(nameof(RtorrentSettings.PiecesPreloadMinRate), x => x.PiecesPreloadMinRate, (x, v) => x.PiecesPreloadMinRate = v),
        ["network_receive_buffer_size"] = Num(nameof(RtorrentSettings.NetworkReceiveBufferSize), x => x.NetworkReceiveBufferSize, (x, v) => x.NetworkReceiveBufferSize = v),
        ["network_send_buffer_size"] = Num(nameof(RtorrentSettings.NetworkSendBufferSize), x => x.NetworkSendBufferSize, (x, v) => x.NetworkSendBufferSize = v),
        ["pieces_sync_always_safe"] = Bool(nameof(RtorrentSettings.PiecesSyncAlwaysSafe), x => x.PiecesSyncAlwaysSafe, (x, v) => x.PiecesSyncAlwaysSafe = v),
        ["pieces_sync_timeout_safe"] = Num(nameof(RtorrentSettings.PiecesSyncTimeoutSafe), x => x.PiecesSyncTimeoutSafe, (x, v) => x.PiecesSyncTimeoutSafe = v),
        ["pieces_sync_timeout"] = Num(nameof(RtorrentSettings.PiecesSyncTimeout), x => x.PiecesSyncTimeout, (x, v) => x.PiecesSyncTimeout = v),
        ["network_scgi_dont_route"] = Bool(nameof(RtorrentSettings.NetworkScgiDontRoute), x => x.NetworkScgiDontRoute, (x, v) => x.NetworkScgiDontRoute = v),

        ["session_path"] = Str(nameof(RtorrentSettings.SessionPath), x => x.SessionPath, (x, v) => x.SessionPath = v),
        ["session_use_lock"] = Bool(nameof(RtorrentSettings.SessionUseLock), x => x.SessionUseLock, (x, v) => x.SessionUseLock = v),
        ["session_on_completion"] = Bool(nameof(RtorrentSettings.SessionOnCompletion), x => x.SessionOnCompletion, (x, v) => x.SessionOnCompletion = v),
        ["system_file_split_size"] = Num(nameof(RtorrentSettings.SystemFileSplitSize), x => x.SystemFileSplitSize, (x, v) => x.SystemFileSplitSize = v),
        ["system_file_split_suffix"] = Str(nameof(RtorrentSettings.SystemFileSplitSuffix), x => x.SystemFileSplitSuffix, (x, v) => x.SystemFileSplitSuffix = v),
        ["trackers_use_udp"] = Bool(nameof(RtorrentSettings.TrackersUseUdp), x => x.TrackersUseUdp, (x, v) => x.TrackersUseUdp = v),

        ["network_http_proxy_address"] = Str(nameof(RtorrentSettings.NetworkHttpProxyAddress), x => x.NetworkHttpProxyAddress, (x, v) => x.NetworkHttpProxyAddress = v),
        ["network_proxy_address"] = Str(nameof(RtorrentSettings.NetworkProxyAddress), x => x.NetworkProxyAddress, (x, v) => x.NetworkProxyAddress = v),
        ["network_bind_address"] = Str(nameof(RtorrentSettings.NetworkBindAddress), x => x.NetworkBindAddress, (x, v) => x.NetworkBindAddress = v)
    };

    public static Models.Settings MapFromProto(RtorrentSettings In)
    {
        var unsupported = In.UnsupportedSettings.ToHashSet(StringComparer.Ordinal);

        var ret = new Models.Settings() {
            General = new Models.General() {
                MaximumMemoryUsage = In.MaximumMemoryUsage,
                CheckHashAfterDownload = In.CheckHashAfterDownload,
                DefaultDirectoryForDownloads = In.DefaultDirectoryForDownloads
            },
            Peers = new Models.Peers() {
                MaximumNumberOfPeers = In.MaximumNumberOfPeers,
                MaximumNumberOfPeersForSeeding = In.MaximumNumberOfPeersForSeeding,
                MinimumNumberOfPeers = In.MinimumNumberOfPeers,
                MinimumNumberOfPeersForSeeding = In.MinimumNumberOfPeersForSeeding,
                NumberOfUploadSlots = In.NumberOfUploadSlots,
                WishedNumberOfPeers = In.WishedNumberOfPeers
            },
            Connection = new Models.Connection() {
                DhtPort = In.DhtPort,
                EnablePeerExchange = In.EnablePeerExchange,
                GlobalNumberOfDownloadSlots = In.GlobalNumberOfDownloadSlots,
                GlobalNumberOfUploadSlots = In.GlobalNumberOfUploadSlots,
                IpToReportToTracker = In.IpToReportToTracker,
                MaximumDownloadRate = In.MaximumDownloadRate,
                MaximumNumberOfOpenFiles = In.MaximumNumberOfOpenFiles,
                MaximumNumberOfOpenHttpConnections = In.MaximumNumberOfOpenHttpConnections,
                MaximumUploadRate = In.MaximumUploadRate,
                OpenListeningPort = In.OpenListeningPort,
                PortUsedForIncomingConnections = In.PortUsedForIncomingConnections,
                RandomizePort = In.RandomizePort,

                SystemSocketsFilesMinAlloc = In.SystemSocketsFilesMinAlloc,
                SystemSocketsFilesMaxAlloc = In.SystemSocketsFilesMaxAlloc,
                SystemSocketsHttpMinAlloc = In.SystemSocketsHttpMinAlloc,
                SystemSocketsHttpMaxAlloc = In.SystemSocketsHttpMaxAlloc,

                OpenListeningPortSupported = !unsupported.Contains(nameof(RtorrentSettings.OpenListeningPort)),
                LegacyConnectionLimitsSupported = !unsupported.Contains(nameof(RtorrentSettings.MaximumNumberOfOpenFiles)),
                SocketAllocationSupported = !unsupported.Contains(nameof(RtorrentSettings.SystemSocketsFilesMinAlloc))
            },
            Advanced = new()
        };

        foreach (var (key, setting) in AdvancedSettings) {
            if (unsupported.Contains(setting.Property))
                continue;

            ret.Advanced.Add(new Models.AdvancedSetting(key, setting.Get(In)));
        }

        return ret;
    }

    public static RtorrentSettings MapToProto(Models.Settings In)
    {
        var ret = new RtorrentSettings() {
            MaximumMemoryUsage = In.General.MaximumMemoryUsage,
            CheckHashAfterDownload = In.General.CheckHashAfterDownload,
            DefaultDirectoryForDownloads = In.General.DefaultDirectoryForDownloads,

            MaximumNumberOfPeers = In.Peers.MaximumNumberOfPeers,
            MaximumNumberOfPeersForSeeding = In.Peers.MaximumNumberOfPeersForSeeding,
            MinimumNumberOfPeers = In.Peers.MinimumNumberOfPeers,
            MinimumNumberOfPeersForSeeding = In.Peers.MinimumNumberOfPeersForSeeding,
            NumberOfUploadSlots = In.Peers.NumberOfUploadSlots,
            WishedNumberOfPeers = In.Peers.WishedNumberOfPeers,

            DhtPort = In.Connection.DhtPort,
            EnablePeerExchange = In.Connection.EnablePeerExchange,
            GlobalNumberOfDownloadSlots = In.Connection.GlobalNumberOfDownloadSlots,
            GlobalNumberOfUploadSlots = In.Connection.GlobalNumberOfUploadSlots,
            IpToReportToTracker = In.Connection.IpToReportToTracker,
            MaximumDownloadRate = In.Connection.MaximumDownloadRate,
            MaximumNumberOfOpenFiles = In.Connection.MaximumNumberOfOpenFiles,
            MaximumNumberOfOpenHttpConnections = In.Connection.MaximumNumberOfOpenHttpConnections,
            MaximumUploadRate = In.Connection.MaximumUploadRate,
            OpenListeningPort = In.Connection.OpenListeningPort,
            PortUsedForIncomingConnections = In.Connection.PortUsedForIncomingConnections,
            RandomizePort = In.Connection.RandomizePort,

            SystemSocketsFilesMinAlloc = In.Connection.SystemSocketsFilesMinAlloc,
            SystemSocketsFilesMaxAlloc = In.Connection.SystemSocketsFilesMaxAlloc,
            SystemSocketsHttpMinAlloc = In.Connection.SystemSocketsHttpMinAlloc,
            SystemSocketsHttpMaxAlloc = In.Connection.SystemSocketsHttpMaxAlloc
        };

        foreach (var advanced in In.Advanced) {
            if (AdvancedSettings.TryGetValue(advanced.Key, out var setting))
                setting.Set(ret, advanced.Value);
        }

        return ret;
    }
}
