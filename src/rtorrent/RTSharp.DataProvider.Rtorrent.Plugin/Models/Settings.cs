using CommunityToolkit.Mvvm.ComponentModel;

using System.Collections.Generic;

namespace RTSharp.DataProvider.Rtorrent.Plugin.Models
{
    public partial class General : ObservableObject
    {
        [ObservableProperty]
        public partial long MaximumMemoryUsage { get; set; }
        [ObservableProperty]
        public partial bool CheckHashAfterDownload { get; set; }
        [ObservableProperty]
        public partial string DefaultDirectoryForDownloads { get; set; }
    }

    public partial class Peers : ObservableObject
    {
        [ObservableProperty]
        public partial long NumberOfUploadSlots { get; set; }
        [ObservableProperty]
        public partial long MinimumNumberOfPeers { get; set; }
        [ObservableProperty]
        public partial long MaximumNumberOfPeers { get; set; }
        [ObservableProperty]
        public partial long MinimumNumberOfPeersForSeeding { get; set; }
        [ObservableProperty]
        public partial long MaximumNumberOfPeersForSeeding { get; set; }
        [ObservableProperty]
        public partial long WishedNumberOfPeers { get; set; }
    }

    public partial class Connection : ObservableObject
    {
        [ObservableProperty]
        public partial long MaximumDownloadRate { get; set; }
        [ObservableProperty]
        public partial long MaximumUploadRate { get; set; }
        [ObservableProperty]
        public partial bool OpenListeningPort { get; set; }
        [ObservableProperty]
        public partial bool RandomizePort { get; set; }
        [ObservableProperty]
        public partial string PortUsedForIncomingConnections { get; set; }
        [ObservableProperty]
        public partial long GlobalNumberOfUploadSlots { get; set; }
        [ObservableProperty]
        public partial long GlobalNumberOfDownloadSlots { get; set; }
        [ObservableProperty]
        public partial long MaximumNumberOfOpenFiles { get; set; }
        [ObservableProperty]
        public partial long MaximumNumberOfOpenHttpConnections { get; set; }
        [ObservableProperty]
        public partial int DhtPort { get; set; }
        [ObservableProperty]
        public partial bool EnablePeerExchange { get; set; }
        [ObservableProperty]
        public partial string IpToReportToTracker { get; set; }
    }

    public class AdvancedSetting : ObservableObject
    {
        public string Key { get; set; }
        public string Value { get; set; }

        public AdvancedSetting(string Key, string Value)
        {
            this.Key = Key;
            this.Value = Value;
        }
    }

    public partial class Settings : ObservableObject
    {
        public required General General { get; set; }
        public required Peers Peers { get; set; }
        public required Connection Connection { get; set; }
        [ObservableProperty]
        public required partial List<AdvancedSetting> Advanced { get; set; }
    }
}
