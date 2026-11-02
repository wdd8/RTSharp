using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.Input;

using RTSharp.CrossSeed.Plugin.ViewModels;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.CrossSeed.Plugin;

public class Plugin : BasePlugin<CrossSeedPluginConfig>
{
    public override string Name => "Cross-seed";

    public override string Description => "Adds a torrent context menu item that finds the same content on other trackers and adds it with fast resume";

    public override string Author => "RTSharp";

    public override int CompatibleMajorVersion => 0;

    public override Guid GUID { get; } = new Guid("3B8F0E52-9A41-4D6C-A7E3-5C21D84F6B90");

    public override PluginCapabilities Capabilities => new PluginCapabilities(
        HasSettingsWindow: true
    );

    public required IPluginHost<CrossSeedPluginConfig> Host { get; set; }

    IDisposable? MenuItem;

    const string MenuHeader = "Find cross-seeds";

    public override Task<IPlugin> Init(IPluginHost<CrossSeedPluginConfig> Host, Action<(string Status, float Percentage)> Progress)
    {
        this.Host = Host;
        Progress(("Initializing...", 0f));

        Dispatcher.UIThread.Invoke(() => {
            MenuItem = this.Host.RegisterTorrentContextMenuItem(list => {
                var newMenuItem = new MenuItem {
                    Header = MenuHeader,
                    Command = new RelayCommand<IReadOnlyList<Torrent>>(selected => Do(selected!))
                };

                list.Add(newMenuItem);
                return newMenuItem;
            }, list => {
                for (var x = 0;x < list.Count;x++) {
                    if (list[x] is MenuItem menuItem && menuItem.Header!.ToString() == MenuHeader) {
                        list.RemoveAt(x);
                    }
                }
            }, ["DuplicateMenu"]);
        });

        Progress(("Done", 100f));

        return Task.FromResult((IPlugin)this);
    }

    public void Do(IReadOnlyList<Torrent> In)
    {
        if (In.Count == 0)
            return;

        var wnd = new MainWindow();
        wnd.ViewModel = new MainWindowViewModel(Host, In);
        wnd.Show();
    }

    public override async Task ShowPluginSettings(object ParentWindow)
    {
        var wnd = new SettingsWindow() {
            ViewModel = new SettingsWindowViewModel(Host)
        };
        await wnd.ShowDialog((Window)ParentWindow);
    }

    public override Task Unload()
    {
        MenuItem?.Dispose();

        return Task.CompletedTask;
    }
}
