using Avalonia.Controls;

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

    public override Task<dynamic> CustomAccess(dynamic In)
    {
        return null;
    }

    public override IPluginHost Host { get; set; }

    public IDataProviderHost DataProvider { get; set; }

    internal ActionQueue ActionQueue { get; set; }

    private CancellationTokenSource DataProviderActive { get; set; }

    public override Task<IPlugin> Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress)
    {
        this.Host = Host;

        Progress(("Registering queue...", 0f));

        ActionQueue = new ActionQueue(Host.PluginInstanceConfig.Name, Host.InstanceId);
        Host.RegisterActionQueue(ActionQueue);

        Progress(("Registering data provider...", 50f));

        DataProvider dp;
        DataProviderActive = new();
        DataProvider = Host.RegisterDataProvider(dp = new DataProvider(this) {
            Active = DataProviderActive.Token
        });

        Progress(("Loaded", 100f));
        return Task.FromResult((IPlugin)this);
    }

    public override async Task ShowPluginSettings(object ParentWindow)
    {
        var settingsWindow = new MainWindow {
            ViewModel = new MainWindowViewModel() {
                PluginHost = Host,
                ThisPlugin = this,
                Title = Host.PluginInstanceConfig.Name + " qbittorrent settings",
                //Settings = SettingsMapper.MapFromProto(settings)
            }
        };
        ((MainWindowViewModel)settingsWindow.DataContext!).ThisWindow = settingsWindow;
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