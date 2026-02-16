using Avalonia.Controls;

using CommunityToolkit.Mvvm.Input;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.MassTrackerRewrite.Plugin;

public class Plugin : BasePlugin
{
    public override string Name => "Mass tracker rewrite";

    public override string Description => "Replaces selected tracker url to something else across all torrents";

    public override string Author => "RTSharp";

    public override Shared.Abstractions.Version Version => new Shared.Abstractions.Version("1.0.0", 1, 0, 0);

    public override int CompatibleMajorVersion => 0;

    public override Guid GUID { get; } = new Guid("01744E45-9609-4D3B-ACF7-AAACAA73C8F3");

    public override PluginCapabilities Capabilities => new PluginCapabilities(
        HasSettingsWindow: false
    );

    public override IPluginHost Host { get; set; }

    IDisposable Hook;

    public override Task<dynamic> CustomAccess(dynamic In) => Task.FromResult<dynamic>(null);

    public override Task<IPlugin> Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress)
    {
        this.Host = Host;
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

        return Task.FromResult((IPlugin)this);
    }

    public override Task ShowPluginSettings(object ParentWindow) => throw new NotImplementedException();

    public override Task Unload()
    {
        Hook?.Dispose();

        return Task.CompletedTask;
    }
}