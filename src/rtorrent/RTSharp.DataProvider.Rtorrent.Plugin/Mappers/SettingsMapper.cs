using System;
using System.Linq;

namespace RTSharp.DataProvider.Rtorrent.Plugin.Mappers;

public static class SettingsMapper
{
    static readonly string[] AdvancedSettings = new[] {
        "network_http_cacert",
        "network_http_capath",
        "throttle_max_downloads_div",
        "throttle_max_uploads_div",
        "system_file_max_size",
        "pieces_preload_type",
        "pieces_preload_min_size",
        "pieces_preload_min_rate",
        "network_receive_buffer_size",
        "network_send_buffer_size",
        "pieces_sync_always_safe",
        "pieces_sync_timeout_safe",
        "pieces_sync_timeout",
        "network_scgi_dont_route",
        "session_path",
        "session_use_lock",
        "session_on_completion",
        "system_file_split_size",
        "system_file_split_suffix",
        "trackers_use_udp",
        "network_http_proxy_address",
        "network_proxy_address",
        "network_bind_address"
    };

    public static Models.Settings MapFromProto(Protocols.Settings In)
    {
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
                RandomizePort = In.RandomizePort
            },
            Advanced = new()
        };

        foreach (var setting in AdvancedSettings) {
            var upperCamel = String.Join("", setting.Split('_').Select(i => Char.ToUpper(i[0]) + i[1..]));

            var val = In.GetType().GetProperty(upperCamel).GetValue(In);
            ret.Advanced.Add(new Models.AdvancedSetting(setting, val.ToString()));
        }

        return ret;
    }

    public static Protocols.Settings MapToProto(Models.Settings In)
    {
        var ret = new Protocols.Settings() {
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
            RandomizePort = In.Connection.RandomizePort
        };

        foreach (var setting in AdvancedSettings) {
            var upperCamel = String.Join("", setting.Split('_').Select(i => Char.ToUpper(i[0]) + i[1..]));

            var val = In.Advanced.First(x => x.Key == setting).Value;
            var prop = ret.GetType().GetProperty(upperCamel);
            if (prop.PropertyType == typeof(long)) {
                prop.SetValue(ret, Int64.Parse(val));
            } else if (prop.PropertyType == typeof(bool)) {
                prop.SetValue(ret, Boolean.Parse(val));
            } else {
                prop.SetValue(ret, val);
            }
        }

        return ret;
    }
}