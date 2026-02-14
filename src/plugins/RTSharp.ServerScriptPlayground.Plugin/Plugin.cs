using Avalonia.Controls;

using CommunityToolkit.Mvvm.Input;

using RTSharp.ServerScriptPlayground.Plugin.Views;
using RTSharp.Shared.Abstractions;

using System;
using System.Threading.Tasks;

namespace RTSharp.ServerScriptPlayground.Plugin
{
    public class Plugin : IPlugin
    {
        public string Name => "Server script playground";

        public string Description => "Runs scripts on RTSharp server";

        public string Author => "RTSharp";

        public Shared.Abstractions.Version Version => new Shared.Abstractions.Version("1.0.0", 1, 0, 0);

        public int CompatibleMajorVersion => 0;

        public Guid GUID { get; } = new Guid("18A2A5EC-C95E-42C9-B555-C87D9764383C");

        public PluginCapabilities Capabilities => new(
            HasSettingsWindow: false
        );

        IDisposable? RootMenuItem;

        public Task<dynamic> CustomAccess(dynamic In) => Task.FromResult<dynamic>(null);

        public Task Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress)
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

            return Task.CompletedTask;
        }

        public Task ShowPluginSettings(object ParentWindow) => throw new NotImplementedException();

        public Task Unload()
        {
            RootMenuItem?.Dispose();

            return Task.CompletedTask;
        }
    }
}