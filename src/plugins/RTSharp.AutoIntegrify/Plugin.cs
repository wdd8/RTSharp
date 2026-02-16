using Avalonia.Controls;

using CommunityToolkit.Mvvm.Input;

using RTSharp.AutoIntegrify.Plugin.ViewModels;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RTSharp.AutoIntegrify.Plugin;

public class Plugin : BasePlugin
{
    public override string Name => "Auto integrify";

    public override string Description => "Automatically searches & verifies file (integrifies!!) to align torrents file integrity";

    public override string Author => "RTSharp";

    public override Shared.Abstractions.Version Version => new Shared.Abstractions.Version("1.0.0", 1, 0, 0);

    public override int CompatibleMajorVersion => 0;

    public override Guid GUID { get; } = new Guid("CBCEEDC7-E236-4780-BFF4-0ADCBACC7870");

    public override PluginCapabilities Capabilities => new PluginCapabilities(
        HasSettingsWindow: true
    );

    IDisposable MenuItem;

    public override IPluginHost Host { get; set; }

    public override Task<dynamic> CustomAccess(dynamic In) => Task.FromResult<dynamic>(null);

    public override Task<IPlugin> Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress)
    {
        this.Host = Host;
        Progress(("Initializing...", 0f));

        Host.RegisterTorrentContextMenuItem(list => {
            var newMenuItem = new MenuItem {
                Header = "Align file integrity & recheck",
                Command = new AsyncRelayCommand<IReadOnlyList<Torrent>>(selected => {
                    return Do(selected!);
                })
            };

            for (var x = 0;x < list.Count; x++) {
                var t = list[x];
                if (t is MenuItem menuItem && menuItem.Header!.ToString() == "Force _recheck") {
                    list.Insert(x + 1, newMenuItem);
                    return newMenuItem;
                }
            }

            list.Add(newMenuItem);
            return newMenuItem;
        }, list => {
            for (var x = 0;x < list.Count;x++) {
                var t = list[x];
                if (t is MenuItem menuItem) {
                    if (menuItem.Header!.ToString() == "Align file integrity & recheck") {
                        list.RemoveAt(x);
                    }
                }
            }
        });

        Progress(("Done", 100f));

        return Task.FromResult((IPlugin)this);
    }

    public async Task Do(IReadOnlyList<Torrent> In)
    {
        if (In.Count != 1)
            return;

        var torrent = In[0];

        var wnd = new MainWindow();
        wnd.ViewModel = new MainWindowViewModel(Host, wnd, torrent);
        wnd.Show();
    }

    public override async Task ShowPluginSettings(object ParentWindow)
    {
        var wnd = new SettingsWindow()
        {
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