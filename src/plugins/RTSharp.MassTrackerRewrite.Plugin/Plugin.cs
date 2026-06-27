using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.Input;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.MassTrackerRewrite.Plugin;

public class Plugin : BasePlugin<PluginConfigContainer<PluginInstanceConfig>>
{
    public override string Name => "Mass tracker rewrite";

    public override string Description => "Replaces selected tracker url to something else across all torrents";

    public override string Author => "RTSharp";

    public override int CompatibleMajorVersion => 0;

    public override Guid GUID { get; } = new Guid("01744E45-9609-4D3B-ACF7-AAACAA73C8F3");

    public override PluginCapabilities Capabilities => new PluginCapabilities(
        HasSettingsWindow: false
    );

    public required IPluginHost<PluginConfigContainer<PluginInstanceConfig>> Host { get; set; }

    IDisposable? Hook;

    public override Task<IPlugin> Init(IPluginHost<PluginConfigContainer<PluginInstanceConfig>> Host, Action<(string Status, float Percentage)> Progress)
    {
        this.Host = Host;
        Progress(("Initializing...", 0f));

        Dispatcher.UIThread.Invoke(() => {
            Hook = this.Host.RegisterToolsMenuItem(new MenuItem() {
                Header = "Mass tracker rewrite",
                Command = new RelayCommand(() => {
                    var wnd = new MainWindow();
                    wnd.ViewModel = new ViewModels.MainWindowViewModel(this.Host, wnd);
                    wnd.Show();
                })
            });
        });

        Progress(("Done", 100f));

        return Task.FromResult((IPlugin)this);
    }

    public override Task Unload()
    {
        Hook?.Dispose();

        return Task.CompletedTask;
    }
}