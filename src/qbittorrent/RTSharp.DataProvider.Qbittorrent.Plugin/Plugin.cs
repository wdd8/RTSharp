using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using Google.Protobuf.WellKnownTypes;

using Nito.Disposables;

using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.DataProvider.Qbittorrent.Plugin.Mappers;
using RTSharp.DataProvider.Qbittorrent.Plugin.ViewModels;
using RTSharp.DataProvider.Qbittorrent.Plugin.Views;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

using System.Reactive.Disposables;

namespace RTSharp.DataProvider.Qbittorrent.Plugin;

public class Plugin : BasePlugin<QbittorrentDataProviderInstanceConfig>
{
    public override string Name => "RTSharp data provider for qbittorrent";

    public override string Description => "RTSharp data provider for qbittorrent";

    public override string Author => "RTSharp";

    public override int CompatibleMajorVersion => 0;

    public override Guid GUID => new Guid("E347109B-A06D-4894-9E3A-6FFF63411370");

    public override PluginCapabilities Capabilities => new PluginCapabilities(
        HasSettingsWindow: true
    );

    public required IDataProviderHost<QbittorrentDataProviderInstanceConfig> Host { get; set; }

    internal DefaultActionQueueRenderer ActionQueue { get; set; } = null!; // init set

    private CancellationTokenSource DataProviderActive { get; set; } = null!; // init set

    private CompositeDisposable Disposables { get; } = new();

    public override async Task<IPlugin> Init(IPluginHost<QbittorrentDataProviderInstanceConfig> Host, Action<(string Status, float Percentage)> Progress)
    {
        Progress(("Registering data provider...", 0f));

        DataProviderActive = new();
        var dp = new DataProvider(this) {
            Active = DataProviderActive.Token
        };
        this.Host = Host.RegisterDataProvider<QbittorrentDataProviderInstanceConfig>(dp);
        Disposables.Add(this.Host);

        Progress(("Registering queue...", 50f));

        await Dispatcher.UIThread.InvokeAsync(() => {
            ActionQueue = new DefaultActionQueueRenderer(
                this.Host.Config.Plugin.Name,
                new Bitmap(AssetLoader.Open(new Uri("avares://RTSharp.DataProvider.Qbittorrent.Plugin/Assets/qbittorrent.png"))));
        });
        Disposables.Add(this.Host.RegisterActionQueue(ActionQueue));

        Progress(("Loaded", 100f));
        return this;
    }

    public override async Task ShowPluginSettings(object ParentWindow)
    {
        var client = Host.AttachedDaemonService!.GetGrpcService<GRPCQBittorrentSettingsService.GRPCQBittorrentSettingsServiceClient>();
        var headers = Host.GetBuiltInDataProviderGrpcHeaders();

        var settings = await client.GetSettingsAsync(new Empty(), headers: headers);
        var ifaces = await client.GetNetworkInterfacesAsync(new Empty(), headers: headers);
        var currentIfaceId = settings.CurrentNetworkInterface ?? "";
        var addrs = await client.GetNetworkInterfaceAddressesAsync(
            new QBittorrentNetworkInterfaceAddressRequest { InterfaceId = currentIfaceId },
            headers: headers);

        var settingsWindow = new MainWindow {
            ViewModel = new MainWindowViewModel() {
                ThisPlugin = this,
                Title = Host.Config.Plugin.Name + " qbittorrent settings",
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
        Disposables.Dispose();
        return Task.CompletedTask;
    }
}
