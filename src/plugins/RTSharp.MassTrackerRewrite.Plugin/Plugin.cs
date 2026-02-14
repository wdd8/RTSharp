using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

using CommunityToolkit.Mvvm.Input;

using RTSharp.Shared.Abstractions;

namespace RTSharp.MassTrackerRewrite.Plugin
{
    public class Plugin : IPlugin
    {
        public string Name => "Mass tracker rewrite";

        public string Description => "Replaces selected tracker url to something else across all torrents";

        public string Author => "RTSharp";

        public Shared.Abstractions.Version Version => new Shared.Abstractions.Version("1.0.0", 1, 0, 0);

        public int CompatibleMajorVersion => 0;

        public Guid GUID { get; } = new Guid("01744E45-9609-4D3B-ACF7-AAACAA73C8F3");

        public PluginCapabilities Capabilities => new PluginCapabilities(
            HasSettingsWindow: false
        );

        IDisposable Hook;

        public Task<dynamic> CustomAccess(dynamic In) => Task.FromResult<dynamic>(null);

        public Task Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress)
        {
            Progress(("Initializing...", 0f));

            Hook = Host.RegisterToolsMenuItem(new MenuItem() {
                Header = "Mass tracker rewrite",
                Command = new RelayCommand(() => {
                    var wnd = new MainWindow();
                    wnd.ViewModel = new ViewModels.MainWindowViewModel(Host, wnd);
                    wnd.Show();
                })
            });

            Progress(("Done", 100f));

            return Task.CompletedTask;
        }

        public Task ShowPluginSettings(object ParentWindow) => throw new NotImplementedException();

        public Task Unload()
        {
            Hook?.Dispose();

            return Task.CompletedTask;
        }
    }
}