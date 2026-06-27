using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using Nito.Disposables;

using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.DataProvider.Rtorrent.Plugin.Mappers;
using RTSharp.DataProvider.Rtorrent.Plugin.ViewModels;
using RTSharp.DataProvider.Rtorrent.Plugin.Views;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

using System;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.DataProvider.Rtorrent.Plugin;

public class Plugin : BasePlugin<RtorrentDataProviderInstanceConfig>
{
    public override string Name => "RTSharp data provider for rtorrent";

    public override string Description => "RTSharp data provider for rtorrent";

    public override string Author => "RTSharp";

    public override int CompatibleMajorVersion => 0;

    public override Guid GUID => new Guid("90F180F2-F1D3-4CAA-859F-06D80B5DCF5C");

    public override PluginCapabilities Capabilities { get; } = new PluginCapabilities(
        HasSettingsWindow: true
    );

    public required IDataProviderHost<RtorrentDataProviderInstanceConfig> Host { get; set; }

    internal DefaultActionQueueRenderer ActionQueue { get; set; } = null!; // init set

    private CancellationTokenSource DataProviderActive { get; set; } = null!; // init set

    private CompositeDisposable Disposables { get; } = new();

    public override async Task<IPlugin> Init(IPluginHost<RtorrentDataProviderInstanceConfig> Host, Action<(string Status, float Percentage)> Progress)
    {
        Progress(("Registering data provider...", 0f));

        DataProviderActive = new();
        var dp = new DataProvider(this)
        {
            Active = DataProviderActive.Token
        };

        this.Host = Host.RegisterDataProvider<RtorrentDataProviderInstanceConfig>(dp);
        Disposables.Add(this.Host);

        Progress(("Registering queue...", 50f));

        await Dispatcher.UIThread.InvokeAsync(() => {
            ActionQueue = new DefaultActionQueueRenderer(
                this.Host.Config.Plugin.Name,
                new Bitmap(AssetLoader.Open(new Uri("avares://RTSharp.DataProvider.Rtorrent.Plugin/Assets/rtorrent.png"))));
        });
        Disposables.Add(this.Host.RegisterActionQueue(ActionQueue));

        Progress(("Loaded", 100f));
        return this;
    }

    public override async Task ShowPluginSettings(object ParentWindow)
    {
        var client = Host.AttachedDaemonService!.GetGrpcService<GRPCRtorrentSettingsService.GRPCRtorrentSettingsServiceClient>();

        var settings = await client.GetSettingsAsync(new Google.Protobuf.WellKnownTypes.Empty(), headers: Host.GetBuiltInDataProviderGrpcHeaders());

        var settingsWindow = new MainWindow {
            ViewModel = new MainWindowViewModel() {
                PluginHost = Host,
                ThisPlugin = this,
                Title = Host.Config.Plugin.Name + " rtorrent settings",
                Settings = SettingsMapper.MapFromProto(settings)
            }
        };
        ((MainWindowViewModel)settingsWindow.DataContext!).ThisWindow = settingsWindow;
        await settingsWindow.ShowDialog((Window)ParentWindow);
    }

    public override Task Unload()
    {
        DataProviderActive?.Cancel();

        Disposables.Dispose();

        return Task.CompletedTask;
    }
}
