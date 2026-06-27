using RTSharp.Shared.Abstractions;
using RTSharp.DataProvider.Transmission.Plugin.Views;
using RTSharp.DataProvider.Transmission.Plugin.ViewModels;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.DataProvider.Transmission.Plugin.Mappers;
using RTSharp.Shared.Abstractions.Client;
using Avalonia.Threading;
using System.Reactive.Disposables;

namespace RTSharp.DataProvider.Transmission.Plugin;

public class Plugin : BasePlugin<TransmissionDataProviderInstanceConfig>
{
    public override string Name => "RTSharp data provider for Transmission";

    public override string Description => "RTSharp data provider for Transmission";

    public override string Author => "RTSharp";

    public override int CompatibleMajorVersion => 0;

    public override Guid GUID => new Guid("0671CE03-09F4-4F07-9402-548CF2E201B1");

    public override PluginCapabilities Capabilities { get; } = new PluginCapabilities(
        HasSettingsWindow: true
    );

    public required IDataProviderHost<TransmissionDataProviderInstanceConfig> Host { get; set; }

    internal DefaultActionQueueRenderer ActionQueue { get; set; } = null!; // on init

    private CancellationTokenSource DataProviderActive { get; set; } = null!; // on init

    private CompositeDisposable Disposables { get; set; } = new();

    public override async Task<IPlugin> Init(IPluginHost<TransmissionDataProviderInstanceConfig> Host, Action<(string Status, float Percentage)> Progress)
    {
        Progress(("Registering data provider...", 0f));

        DataProviderActive = new();
        var dp = new DataProvider(this) {
            Active = DataProviderActive.Token
        };
        this.Host = Host.RegisterDataProvider<TransmissionDataProviderInstanceConfig>(dp);
        Disposables.Add(this.Host);

        Progress(("Registering queue...", 50f));

        await Dispatcher.UIThread.InvokeAsync(() => {
            ActionQueue = new DefaultActionQueueRenderer(
                this.Host.Config.Plugin.Name,
                new Bitmap(AssetLoader.Open(new Uri("avares://RTSharp.DataProvider.Transmission.Plugin/Assets/transmission.png"))));
        });
        Disposables.Add(this.Host.RegisterActionQueue(ActionQueue));

        Progress(("Loaded", 100f));
        return this;
    }

    public override async Task ShowPluginSettings(object ParentWindow)
    {
        var client = Host.AttachedDaemonService!.GetGrpcService<GRPCTransmissionSettingsService.GRPCTransmissionSettingsServiceClient>();

        var settings = await client.GetSessionInformationAsync(new Google.Protobuf.WellKnownTypes.Empty(), headers: Host.GetBuiltInDataProviderGrpcHeaders());

        var settingsWindow = new MainWindow {
            ViewModel = new MainWindowViewModel() {
                ThisPlugin = this,
                Title = Host.Config.Plugin.Name + " transmission settings",
                Settings = SettingsMapper.MapFromProto(settings),
                ListUpdateIntervalSeconds = Host.Config.ListUpdateInterval.TotalSeconds
            }
        };
        ((MainWindowViewModel)settingsWindow.DataContext!).ThisWindow = settingsWindow;
        await settingsWindow.ShowDialog((Window)ParentWindow);
    }

    public override Task Unload()
    {
        DataProviderActive.Cancel();
        Disposables.Dispose();

        return Task.CompletedTask;
    }
}
