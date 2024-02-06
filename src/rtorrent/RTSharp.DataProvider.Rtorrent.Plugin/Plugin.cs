using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using RTSharp.DataProvider.Rtorrent.Plugin.Mappers;
using RTSharp.DataProvider.Rtorrent.Plugin.Server;
using RTSharp.DataProvider.Rtorrent.Plugin.ViewModels;
using RTSharp.DataProvider.Rtorrent.Plugin.Views;
using RTSharp.Shared.Abstractions;
using Version = RTSharp.Shared.Abstractions.Version;

namespace RTSharp.DataProvider.Rtorrent.Plugin
{
    public class Plugin : IPlugin
    {
        public string Name => "RTSharp data provider for rtorrent";

        public string Description => "RTSharp data provider for rtorrent";

        public string Author => "RTSharp";

        public Version Version => new("0.0.1", 0, 0, 1);

        public int CompatibleMajorVersion => 0;

        public Guid GUID => new Guid("90F180F2-F1D3-4CAA-859F-06D80B5DCF5C");

        public PluginCapabilities Capabilities { get; } = new PluginCapabilities(
            HasSettingsWindow: true
        );

        public Task<dynamic> CustomAccess(dynamic In)
        {
            return null;
        }

        public IPluginHost Host { get; private set; }

        private object DataProvider { get; set; }

        internal ActionQueue ActionQueue { get; set; }

        public async Task Init(IPluginHost Host, IProgress<(string Status, float Percentage)> Progress)
        {
            this.Host = Host;

            if (String.IsNullOrEmpty(Host.PluginConfig.GetServerUri())) {
                throw new InvalidOperationException("Failed to load, missing required config values");
            }

            Progress.Report(("Registering queue...", 0f));

            ActionQueue = new ActionQueue(Host.PluginInstanceConfig.Name, Host.InstanceId);
            Host.RegisterActionQueue(ActionQueue);

            Progress.Report(("Registering data provider...", 50f));

            var dp = new DataProvider(this);

            _ = Server.Clients.Initialize(Host).ContinueWith(task => {
                _ = dp.UpdateLatency();
                DataProvider = Host.RegisterDataProvider(dp);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);

            Progress.Report(("Loaded", 100f));
        }

        public async Task ShowPluginSettings(object ParentWindow)
        {
            var client = Clients.Settings();
            var settings = await client.GetSettingsAsync(new Google.Protobuf.WellKnownTypes.Empty());

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

        public Task Unload()
        {
            Host?.UnregisterDataProvider(DataProvider);
            Host?.UnregisterActionQueue();
            return Task.CompletedTask;
        }
    }
}
