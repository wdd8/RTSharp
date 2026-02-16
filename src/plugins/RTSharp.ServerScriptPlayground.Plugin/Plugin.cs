using Avalonia.Controls;

using CommunityToolkit.Mvvm.Input;

using RTSharp.ServerScriptPlayground.Plugin.Views;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

using System;
using System.Threading.Tasks;

namespace RTSharp.ServerScriptPlayground.Plugin;

public class Plugin : BasePlugin
{
    public override string Name => "Server script playground";

    public override string Description => "Runs scripts on RTSharp server";

    public override string Author => "RTSharp";

    public override Shared.Abstractions.Version Version => new Shared.Abstractions.Version("1.0.0", 1, 0, 0);

    public override int CompatibleMajorVersion => 0;

    public override Guid GUID { get; } = new Guid("18A2A5EC-C95E-42C9-B555-C87D9764383C");

    public override PluginCapabilities Capabilities => new(
        HasSettingsWindow: false
    );

    public override IPluginHost Host { get; set; }

    IDisposable? RootMenuItem;

    public override Task<dynamic> CustomAccess(dynamic In) => Task.FromResult<dynamic>(null);

    public override Task<IPlugin> Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress)
    {
        Progress(("Initializing...", 0f));

        RootMenuItem = Host.RegisterRootMenuItem(new MenuItem() {
            Header = "Server scripting",
            Command = new RelayCommand(() => {
                var wnd = new PlaygroundWindow();
                wnd.ViewModel = new ViewModels.PlaygroundWindowViewModel(Host, wnd);
                wnd.Show();
            })
        });

        Progress(("Done", 100f));

        return Task.FromResult((IPlugin)this);
    }

    public override Task ShowPluginSettings(object ParentWindow) => throw new NotImplementedException();

    public override Task Unload()
    {
        RootMenuItem?.Dispose();

        return Task.CompletedTask;
    }
}