using RTSharp.Shared.Abstractions;
using RTSharp.DataProvider.Transmission.Plugin.Views;
using RTSharp.DataProvider.Transmission.Plugin.ViewModels;
using Avalonia.Controls;

namespace RTSharp.DataProvider.Transmission.Plugin
{
    public class Plugin : IPlugin
    {
        public string Name => "RTSharp data provider for Transmission";

        public string Description => "RTSharp data provider for Transmission";

        public string Author => "RTSharp";

        public Shared.Abstractions.Version Version => new("0.0.1", 0, 0, 1);

        public int CompatibleMajorVersion => 0;

        public Guid GUID => new Guid("0671CE03-09F4-4F07-9402-548CF2E201B1");

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
                    Title = Host.PluginInstanceConfig.Name + " transmission settings",
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
