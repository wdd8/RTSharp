using System.Collections.ObjectModel;

using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;


using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.DataProvider.Qbittorrent.Plugin.Mappers;
using RTSharp.DataProvider.Qbittorrent.Plugin.Models;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.DataProvider.Qbittorrent.Plugin.ViewModels;

public record Category(string Name, string Icon);
public record NetworkInterfaceItem(string Id, string DisplayName);

public partial class MainWindowViewModel : ObservableObject
{
    private const string AnyAddressDisplay = "(Any address)";

    public MainWindowViewModel()
    {
        Settings = new Settings();
        CurrentlySelectedCategory = Categories[0];
    }

    [RelayCommand]
    public void RandomizePort()
    {
        Settings.Connection.ListenPort = Random.Shared.Next(1024, 65536);
    }

    [RelayCommand]
    public async Task SaveSettingsClick()
    {
        SavingSettings = true;
        var client = PluginHost.AttachedDaemonService!.GetGrpcService<GRPCQBittorrentSettingsService.GRPCQBittorrentSettingsServiceClient>();

        Func<ActionQueueAction, Task<Empty>> setSettingsTask = async (task) =>
            await client.SetSettingsAsync(SettingsMapper.MapToProto(Settings), headers: ThisPlugin.DataProvider.GetBuiltInDataProviderGrpcHeaders());

        var action = ActionQueueAction.New<Empty>("Set preferences", setSettingsTask);
        _ = action.CreateChild("Wait for completion", RUN_MODE.DEPENDS_ON_PARENT, async (parent, action) => {
            SavingSettings = false;
        });
        await ((IActionQueueRenderer)ThisPlugin.ActionQueue).RunAction(action);
    }

    async partial void OnSavingSettingsChanged(bool value)
    {
        if (!value) {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        SaveSettingsEnabled = !value;
    }

    [ObservableProperty]
    public partial Models.ScanDirectory? SelectedScanDirectory { get; set; }

    [RelayCommand]
    public void AddScanDirectory()
    {
        var entry = new Models.ScanDirectory();
        Settings.Downloads.ScanDirectories.Add(entry);
        SelectedScanDirectory = entry;
    }

    [RelayCommand]
    public void RemoveScanDirectory()
    {
        if (SelectedScanDirectory != null)
            Settings.Downloads.ScanDirectories.Remove(SelectedScanDirectory);
    }

    // Network interface / address support

    public ObservableCollection<NetworkInterfaceItem> NetworkInterfaces { get; } = new();
    public ObservableCollection<string> InterfaceAddresses { get; } = new();

    [ObservableProperty]
    public partial NetworkInterfaceItem? SelectedNetworkInterface { get; set; }

    [ObservableProperty]
    public partial string? SelectedInterfaceAddress { get; set; }

    private bool _networkDataInitialized;

    partial void OnSelectedNetworkInterfaceChanged(NetworkInterfaceItem? value)
    {
        Settings.Advanced.CurrentNetworkInterface = value?.Id ?? "";
        if (_networkDataInitialized && PluginHost != null)
            _ = LoadInterfaceAddressesAsync();
    }

    partial void OnSelectedInterfaceAddressChanged(string? value)
    {
        if (_networkDataInitialized)
            Settings.Advanced.CurrentInterfaceAddress = value == AnyAddressDisplay ? "" : (value ?? "");
    }

    public async Task InitializeNetworkDataAsync(
        IEnumerable<QBittorrentNetworkInterface> ifaces,
        IEnumerable<string> addrs)
    {
        _networkDataInitialized = false;

        NetworkInterfaces.Clear();
        NetworkInterfaces.Add(new NetworkInterfaceItem("", "Any interface"));
        foreach (var i in ifaces)
            NetworkInterfaces.Add(new NetworkInterfaceItem(i.Id, i.Name));

        InterfaceAddresses.Clear();
        InterfaceAddresses.Add(AnyAddressDisplay);
        foreach (var a in addrs)
            InterfaceAddresses.Add(a);

        var currentIface = Settings.Advanced.CurrentNetworkInterface ?? "";
        var currentAddr = Settings.Advanced.CurrentInterfaceAddress ?? "";

        SelectedNetworkInterface = NetworkInterfaces.FirstOrDefault(x => x.Id == currentIface) ?? NetworkInterfaces[0];
        SelectedInterfaceAddress = InterfaceAddresses.Contains(currentAddr) ? currentAddr
            : (string.IsNullOrEmpty(currentAddr) ? AnyAddressDisplay : currentAddr);

        _networkDataInitialized = true;
    }

    private async Task LoadInterfaceAddressesAsync()
    {
        var client = PluginHost.AttachedDaemonService!.GetGrpcService<GRPCQBittorrentSettingsService.GRPCQBittorrentSettingsServiceClient>();
        try {
            var result = await client.GetNetworkInterfaceAddressesAsync(
                new QBittorrentNetworkInterfaceAddressRequest { InterfaceId = Settings.Advanced.CurrentNetworkInterface ?? "" },
                headers: ThisPlugin.DataProvider.GetBuiltInDataProviderGrpcHeaders());

            InterfaceAddresses.Clear();
            InterfaceAddresses.Add(AnyAddressDisplay);
            foreach (var addr in result.Addresses)
                InterfaceAddresses.Add(addr);

            if (SelectedInterfaceAddress != null && !InterfaceAddresses.Contains(SelectedInterfaceAddress))
                SelectedInterfaceAddress = AnyAddressDisplay;
        } catch { }
    }

    [ObservableProperty]
    public partial bool SavingSettings { get; set; }

    [ObservableProperty]
    public partial bool SaveSettingsEnabled { get; set; } = true;

    public required Plugin ThisPlugin { private get; init; }
    public required IPluginHost PluginHost { get; init; }

    public Window ThisWindow { get; set; } = null!; // init set

    public required string Title { get; init; }

    [ObservableProperty]
    public partial Settings Settings { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BehaviourSelected))]
    [NotifyPropertyChangedFor(nameof(DownloadsSelected))]
    [NotifyPropertyChangedFor(nameof(ConnectionSelected))]
    [NotifyPropertyChangedFor(nameof(SpeedSelected))]
    [NotifyPropertyChangedFor(nameof(BitTorrentSelected))]
    [NotifyPropertyChangedFor(nameof(RssSelected))]
    [NotifyPropertyChangedFor(nameof(WebUISelected))]
    [NotifyPropertyChangedFor(nameof(AdvancedSelected))]
    public partial Category CurrentlySelectedCategory { get; set; }

    public bool BehaviourSelected => CurrentlySelectedCategory?.Name == "Behaviour";
    public bool DownloadsSelected => CurrentlySelectedCategory?.Name == "Downloads";
    public bool ConnectionSelected => CurrentlySelectedCategory?.Name == "Connection";
    public bool SpeedSelected => CurrentlySelectedCategory?.Name == "Speed";
    public bool BitTorrentSelected => CurrentlySelectedCategory?.Name == "BitTorrent";
    public bool RssSelected => CurrentlySelectedCategory?.Name == "RSS";
    public bool WebUISelected => CurrentlySelectedCategory?.Name == "WebUI";
    public bool AdvancedSelected => CurrentlySelectedCategory?.Name == "Advanced";

    public static Category[] Categories { get; set; } = [
        new Category("Behaviour", "fa7-solid fa7-sliders"),
        new Category("Downloads", "fa7-solid fa7-download"),
        new Category("Connection", "fa7-solid fa7-network-wired"),
        new Category("Speed", "fa7-solid fa7-gauge"),
        new Category("BitTorrent", "fa7-solid fa7-share-nodes"),
        new Category("RSS", "fa7-solid fa7-rss"),
        new Category("WebUI", "fa7-solid fa7-globe"),
        new Category("Advanced", "fa7-solid fa7-screwdriver-wrench")
    ];

    public static string[] LogAgeTypeOptions => SettingsMapper.LogAgeTypeOptions;
    public static string[] ScanDirectoryActionOptions => SettingsMapper.ScanDirectoryActionOptions;
    public static string[] ScanDirectoryComboOptions { get; } = [..SettingsMapper.ScanDirectoryActionOptions, "Other..."];
    public static string[] TMMDefaultModeOptions => SettingsMapper.TMMDefaultModeOptions;
    public static string[] TMMCategoryChangeOptions => SettingsMapper.TMMCategoryChangeOptions;
    public static string[] TMMPathChangeOptions => SettingsMapper.TMMPathChangeOptions;
    public static string[] BittorrentProtocolOptions => SettingsMapper.BittorrentProtocolOptions;
    public static string[] EncryptionOptions => SettingsMapper.EncryptionOptions;
    public static string[] ProxyTypeOptions => SettingsMapper.ProxyTypeOptions;
    public static string[] MaxRatioActionOptions => SettingsMapper.MaxRatioActionOptions;
    public static string[] SchedulerDaysOptions => SettingsMapper.SchedulerDaysOptions;
    public static string[] ChokingAlgorithmOptions => SettingsMapper.UploadSlotsChokingAlgorithmOptions;
    public static string[] SeedChokingAlgorithmOptions => SettingsMapper.SeedChokingAlgorithmOptions;
    public static string[] UtpTcpMixedModeOptions => SettingsMapper.UtpTcpMixedModeOptions;
    public static string[] DynamicDnsServiceOptions => SettingsMapper.DynamicDnsServiceOptions;
    public static string[] ResumeDataStorageTypeOptions => SettingsMapper.ResumeDataStorageTypeOptions;
    public static string[] TorrentContentRemoveOptions => SettingsMapper.TorrentContentRemoveOptions;
    public static string[] DiskIOReadModeOptions => SettingsMapper.DiskIOReadModeOptions;
    public static string[] DiskIOWriteModeOptions => SettingsMapper.DiskIOWriteModeOptions;
    public static string[] TorrentContentLayoutOptions => SettingsMapper.TorrentContentLayoutOptions;
    public static string[] TorrentStopConditionOptions => SettingsMapper.TorrentStopConditionOptions;

}
