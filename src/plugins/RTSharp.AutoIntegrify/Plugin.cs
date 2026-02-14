using Avalonia.Controls;
using Avalonia.Styling;

using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Hosting;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.AutoIntegrify.Plugin.ViewModels;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RTSharp.AutoIntegrify.Plugin
{
    public class Plugin : IPlugin
    {
        public string Name => "Auto integrify";

        public string Description => "Automatically searches & verifies file (integrifies!!) to align torrents file integrity";

        public string Author => "RTSharp";

        public Shared.Abstractions.Version Version => new Shared.Abstractions.Version("1.0.0", 1, 0, 0);

        public int CompatibleMajorVersion => 0;

        public Guid GUID { get; } = new Guid("CBCEEDC7-E236-4780-BFF4-0ADCBACC7870");

        public PluginCapabilities Capabilities => new PluginCapabilities(
            HasSettingsWindow: true
        );

        IDisposable MenuItem;

        IPluginHost Host;

        public Task<dynamic> CustomAccess(dynamic In) => Task.FromResult<dynamic>(null);

        public Task Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress)
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

            return Task.CompletedTask;
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

        public async Task ShowPluginSettings(object ParentWindow)
        {
            var wnd = new SettingsWindow()
            {
                ViewModel = new SettingsWindowViewModel(Host)
            };
            await wnd.ShowDialog((Window)ParentWindow);
        }

        public Task Unload()
        {
            MenuItem?.Dispose();

            return Task.CompletedTask;
        }
    }
}