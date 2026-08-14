using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.Input;

using RTSharp.ServerScriptPlayground.Plugin.Views;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

using System;
using System.Threading.Tasks;

namespace RTSharp.ServerScriptPlayground.Plugin;

public class Plugin : BasePlugin<PluginConfigContainer<PluginInstanceConfig>>
{
    public override string Name => "Server script playground";

    public override string Description => "Runs scripts on RTSharp server";

    public override string Author => "RTSharp";

    public override int CompatibleMajorVersion => 0;

    public override Guid GUID { get; } = new Guid("18A2A5EC-C95E-42C9-B555-C87D9764383C");

    public override PluginCapabilities Capabilities => new(
        HasSettingsWindow: false
    );

    public required IPluginHost<PluginConfigContainer<PluginInstanceConfig>> Host { get; set; }

    IDisposable? RootMenuItem;

    public override Task<IPlugin> Init(IPluginHost<PluginConfigContainer<PluginInstanceConfig>> Host, Action<(string Status, float Percentage)> Progress)
    {
        this.Host = Host;
        Progress(("Initializing...", 0f));

        Dispatcher.UIThread.Invoke(() => {
            RootMenuItem = this.Host.RegisterRootMenuItem(new MenuItem() {
                Header = "Server scripting",
                Command = new RelayCommand(() => {
                    var wnd = new PlaygroundWindow();
                    wnd.ViewModel = new ViewModels.PlaygroundWindowViewModel(this.Host, wnd, GUID);
                    wnd.Show();
                })
            });
        });

        Progress(("Done", 100f));

        return Task.FromResult((IPlugin)this);
    }

    public override Task Unload()
    {
        RootMenuItem?.Dispose();

        return Task.CompletedTask;
    }
}