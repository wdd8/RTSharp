using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.DataProvider.Qbittorrent.Plugin.Mappers;
using RTSharp.DataProvider.Qbittorrent.Plugin.ViewModels;
using RTSharp.DataProvider.Qbittorrent.Plugin.Views;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.DataProvider.Qbittorrent.Plugin;

public class Plugin : BasePlugin
{
    public override string Name => "RTSharp data provider for qbittorrent";

    public override string Description => "RTSharp data provider for qbittorrent";

    public override string Author => "RTSharp";

    public override Shared.Abstractions.Version Version => new("0.0.1", 0, 0, 1);

    public override int CompatibleMajorVersion => 0;

    public override Guid GUID => new Guid("E347109B-A06D-4894-9E3A-6FFF63411370");

    public override PluginCapabilities Capabilities => new PluginCapabilities(
        HasSettingsWindow: true
    );

    public override Task<dynamic?> CustomAccess(dynamic? In)
    {
        return null!;
    }

    public required override IPluginHost Host { get; set; }

    public required IDataProviderHost DataProvider { get; set; }

    internal DefaultActionQueueRenderer ActionQueue { get; set; } = null!; // init set

    private CancellationTokenSource DataProviderActive { get; set; } = null!; // init set

    public override async Task<IPlugin> Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress)
    {
        this.Host = Host;

        Progress(("Registering queue...", 0f));

        await Dispatcher.UIThread.InvokeAsync(() => {
            ActionQueue = new DefaultActionQueueRenderer(
                Host.PluginInstanceConfig.Name,
                new Bitmap(AssetLoader.Open(new Uri("avares://RTSharp.DataProvider.Qbittorrent.Plugin/Assets/qbittorrent.png"))));
        });
        Host.RegisterActionQueue(ActionQueue);

        Progress(("Registering data provider...", 50f));

        DataProvider dp;
        DataProviderActive = new();
        DataProvider = Host.RegisterDataProvider(dp = new DataProvider(this) {
            Active = DataProviderActive.Token
        });

        Progress(("Loaded", 100f));
        return this;
    }

    public override async Task ShowPluginSettings(object ParentWindow)
    {
        var client = Host.AttachedDaemonService!.GetGrpcService<GRPCQBittorrentSettingsService.GRPCQBittorrentSettingsServiceClient>();
        var headers = DataProvider.GetBuiltInDataProviderGrpcHeaders();

        var settings = await client.GetSettingsAsync(new Empty(), headers: headers);
        var ifaces = await client.GetNetworkInterfacesAsync(new Empty(), headers: headers);
        var currentIfaceId = settings.CurrentNetworkInterface ?? "";
        var addrs = await client.GetNetworkInterfaceAddressesAsync(
            new QBittorrentNetworkInterfaceAddressRequest { InterfaceId = currentIfaceId },
            headers: headers);

        var settingsWindow = new MainWindow {
            ViewModel = new MainWindowViewModel() {
                PluginHost = Host,
                ThisPlugin = this,
                Title = Host.PluginInstanceConfig.Name + " qbittorrent settings",
                Settings = SettingsMapper.MapFromProto(settings)
            }
        };
        var vm = (MainWindowViewModel)settingsWindow.DataContext!;
        vm.ThisWindow = settingsWindow;
        await vm.InitializeNetworkDataAsync(ifaces.Interfaces, addrs.Addresses);
        await settingsWindow.ShowDialog((Window)ParentWindow);
    }

    public override Task Unload()
    {
        DataProviderActive.Cancel();
        Host?.UnregisterDataProvider(DataProvider);
        Host?.UnregisterActionQueue();
        return Task.CompletedTask;
    }
}
