using CommunityToolkit.Mvvm.ComponentModel;

using System.Collections.Generic;

namespace RTSharp.DataProvider.Rtorrent.Plugin.Models
{
	public partial class General : ObservableObject
	{
		[ObservableProperty]
		public long maximumMemoryUsage;
		[ObservableProperty]
		public bool checkHashAfterDownload;
		[ObservableProperty]
		public string defaultDirectoryForDownloads;
	}

	public partial class Peers : ObservableObject
	{
		[ObservableProperty]
		public long numberOfUploadSlots;
		[ObservableProperty]
		public long minimumNumberOfPeers;
		[ObservableProperty]
		public long maximumNumberOfPeers;
		[ObservableProperty]
		public long minimumNumberOfPeersForSeeding;
		[ObservableProperty]
		public long maximumNumberOfPeersForSeeding;
		[ObservableProperty]
		public long wishedNumberOfPeers;
	}

	public partial class Connection : ObservableObject
	{
		[ObservableProperty]
		public long maximumDownloadRate;
		[ObservableProperty]
		public long maximumUploadRate;
		[ObservableProperty]
		public bool openListeningPort;
		[ObservableProperty]
		public bool randomizePort;
		[ObservableProperty]
		public string portUsedForIncomingConnections;
		[ObservableProperty]
		public long globalNumberOfUploadSlots;
		[ObservableProperty]
		public long globalNumberOfDownloadSlots;
		[ObservableProperty]
		public long maximumNumberOfOpenFiles;
		[ObservableProperty]
		public long maximumNumberOfOpenHttpConnections;
		[ObservableProperty]
		public int dhtPort;
		[ObservableProperty]
		public bool enablePeerExchange;
		[ObservableProperty]
		public string ipToReportToTracker;
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
		public General General { get; set; }
		public Peers Peers { get; set; }
		public Connection Connection { get; set; }
		[ObservableProperty]
		public List<AdvancedSetting> advanced;
	}
}
