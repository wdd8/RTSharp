using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.DataProvider.Transmission.Plugin.Models
{
    public partial class Download : ObservableObject
    {
        [ObservableProperty]
        public int? cacheSizeMB; //

        [ObservableProperty]
        public string? downloadDirectory; //

        [ObservableProperty]
        public string? incompleteDirectory; //

        [ObservableProperty]
        public bool incompleteDirectoryEnabled; //

        [ObservableProperty]
        public bool renamePartialFiles; //

        [ObservableProperty]
        public double? seedRatioLimit; //

        [ObservableProperty]
        public bool seedRatioLimited; //

        [ObservableProperty]
        public int? idleSeedingLimit; //

        [ObservableProperty]
        public bool idleSeedingLimitEnabled; //
    }

    public partial class Network : ObservableObject
    {
        [ObservableProperty]
        public string? blocklistURL; //

        [ObservableProperty]
        public bool blocklistEnabled; //

        [ObservableProperty]
        public bool dHTEnabled; //

        [ObservableProperty]
        public bool lPDEnabled; //

        [ObservableProperty]
        public bool pexEnabled; //

        [ObservableProperty]
        public bool utpEnabled; //

        [ObservableProperty]
        public int? peerLimitGlobal; //

        [ObservableProperty]
        public int? peerLimitPerTorrent; //

        [ObservableProperty]
        public int peerPort; //

        [ObservableProperty]
        public bool peerPortRandomOnStart; //

        [ObservableProperty]
        public bool portForwardingEnabled; //

        [ObservableProperty]
        public string? encryption; //
    }

    public partial class Bandwidth : ObservableObject
    {
        [ObservableProperty]
        public int? alternativeSpeedDown; //

        [ObservableProperty]
        public bool alternativeSpeedEnabled; //

        [ObservableProperty]
        public int? alternativeSpeedTimeBegin; //

        [ObservableProperty]
        public bool alternativeSpeedTimeEnabled; //

        [ObservableProperty]
        public int? alternativeSpeedTimeEnd; //

        [ObservableProperty]
        public bool alternativeSpeedTimeDay1;
        [ObservableProperty]
        public bool alternativeSpeedTimeDay2;
        [ObservableProperty]
        public bool alternativeSpeedTimeDay3;
        [ObservableProperty]
        public bool alternativeSpeedTimeDay4;
        [ObservableProperty]
        public bool alternativeSpeedTimeDay5;
        [ObservableProperty]
        public bool alternativeSpeedTimeDay6;
        [ObservableProperty]
        public bool alternativeSpeedTimeDay7;

        [ObservableProperty]
        public int? alternativeSpeedUp; //

        [ObservableProperty]
        public int? speedLimitDown; //

        [ObservableProperty]
        public bool speedLimitDownEnabled; //

        [ObservableProperty]
        public int? speedLimitUp; //

        [ObservableProperty]
        public bool speedLimitUpEnabled; //
    }

    public partial class Queue : ObservableObject
    {
        [ObservableProperty]
        public bool queueStalledEnabled; //

        [ObservableProperty]
        public int? queueStalledMinutes; //

        [ObservableProperty]
        public int? seedQueueSize; //

        [ObservableProperty]
        public bool seedQueueEnabled; //

        [ObservableProperty]
        public int? downloadQueueSize; //

        [ObservableProperty]
        public bool downloadQueueEnabled; //
    }

    public partial class Settings : ObservableObject
    {
        public Download Download { get; set; } = new Download();
        public Network Network { get; set; } = new Network();
        public Bandwidth Bandwidth { get; set; } = new Bandwidth();
        public Queue Queue { get; set; } = new Queue();
    }
}
