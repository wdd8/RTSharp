using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.DataProvider.Rtorrent.Plugin.Mappers;
using RTSharp.DataProvider.Rtorrent.Plugin.ViewModels;
using RTSharp.DataProvider.Rtorrent.Plugin.Views;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

using Version = RTSharp.Shared.Abstractions.Version;

namespace RTSharp.DataProvider.Rtorrent.Plugin;

public class Plugin : BasePlugin
{
    public override string Name => "RTSharp data provider for rtorrent";

    public override string Description => "RTSharp data provider for rtorrent";

    public override string Author => "RTSharp";

    public override Version Version => new("0.0.1", 0, 0, 1);

    public override int CompatibleMajorVersion => 0;

    public override Guid GUID => new Guid("90F180F2-F1D3-4CAA-859F-06D80B5DCF5C");

    public override PluginCapabilities Capabilities { get; } = new PluginCapabilities(
        HasSettingsWindow: true
    );

    public override Task<dynamic> CustomAccess(dynamic In)
    {
        return null;
    }

    public override IPluginHost Host { get; set; }

    public IDataProviderHost DataProvider { get; set; }

    internal DefaultActionQueueRenderer ActionQueue { get; set; }

    private CancellationTokenSource DataProviderActive { get; set; }

    public override async Task<IPlugin> Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress)
    {
        this.Host = Host;
        Progress(("Registering queue...", 0f));

        await Dispatcher.UIThread.InvokeAsync(() => {
            ActionQueue = new DefaultActionQueueRenderer(
                Host.PluginInstanceConfig.Name,
                new Bitmap(AssetLoader.Open(new Uri("avares://RTSharp.DataProvider.Rtorrent.Plugin/Assets/rtorrent.png"))));
        });
        Host.RegisterActionQueue(ActionQueue);

        Progress(("Registering data provider...", 50f));

        DataProviderActive = new();
        var dp = new DataProvider(this)
        {
            Active = DataProviderActive.Token
        };

        DataProvider = Host.RegisterDataProvider(dp);

        Progress(("Loaded", 100f));
        return this;
    }

    public override async Task ShowPluginSettings(object ParentWindow)
    {
        var daemon = Host.AttachedDaemonService;
        var client = daemon.GetGrpcService<GRPCRtorrentSettingsService.GRPCRtorrentSettingsServiceClient>();

        var settings = await client.GetSettingsAsync(new Google.Protobuf.WellKnownTypes.Empty(), headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());

        var settingsWindow = new MainWindow {
            ViewModel = new MainWindowViewModel() {
                PluginHost = Host,
                ThisPlugin = this,
                Title = Host.PluginInstanceConfig.Name + " rtorrent settings",
                Settings = SettingsMapper.MapFromProto(settings)
            }
        };
        ((MainWindowViewModel)settingsWindow.DataContext).ThisWindow = settingsWindow;
        await settingsWindow.ShowDialog((Window)ParentWindow);
    }

    public override Task Unload()
    {
        DataProviderActive?.Cancel();
        Host?.UnregisterDataProvider(DataProvider);
        Host?.UnregisterActionQueue();
        return Task.CompletedTask;
    }
}
