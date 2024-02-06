using Avalonia.Controls;

using RTSharp.DataProvider.Qbittorrent.Plugin.ViewModels;
using RTSharp.DataProvider.Qbittorrent.Plugin.Views;
using RTSharp.Shared.Abstractions;

namespace RTSharp.DataProvider.Qbittorrent.Plugin
{
    public class Plugin : IPlugin
    {
        public string Name => "RTSharp data provider for qbittorrent";

        public string Description => "RTSharp data provider for qbittorrent";

        public string Author => "RTSharp";

        public Shared.Abstractions.Version Version => new("0.0.1", 0, 0, 1);

        public int CompatibleMajorVersion => 0;

        public Guid GUID => new Guid("E347109B-A06D-4894-9E3A-6FFF63411370");

        public PluginCapabilities Capabilities => new PluginCapabilities(
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

            Progress.Report(("Registering queue...", 0f));

            ActionQueue = new ActionQueue(Host.PluginInstanceConfig.Name, Host.InstanceId);
            Host.RegisterActionQueue(ActionQueue);

            Progress.Report(("Registering data provider...", 50f));

            DataProvider dp;
            DataProvider = Host.RegisterDataProvider(dp = new DataProvider(this));

            Progress.Report(("Loaded", 100f));
        }

        public async Task ShowPluginSettings(object ParentWindow)
        {
            var settingsWindow = new MainWindow {
                ViewModel = new MainWindowViewModel() {
                    PluginHost = Host,
                    ThisPlugin = this,
                    Title = Host.PluginInstanceConfig.Name + " qbittorrent settings",
                    //Settings = SettingsMapper.MapFromProto(settings)
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