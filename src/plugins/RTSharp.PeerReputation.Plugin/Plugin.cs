using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.Input;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Scripts;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Abstractions.Daemon;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace RTSharp.PeerReputation.Plugin;

public class Plugin : BasePlugin<PluginConfigContainer<PluginInstanceConfig>>
{
    public override string Name => "Peer Reputation";

    public override string Description => "Track connectivity to peers";

    public override string Author => "RTSharp";

    public override int CompatibleMajorVersion => 0;

    public override Guid GUID { get; } = new Guid("7A247D5A-E6A2-4F0E-8B69-79B2672FA0F3");

    public override PluginCapabilities Capabilities => new PluginCapabilities(
        HasSettingsWindow: false
    );

    public required IPluginHost<PluginConfigContainer<PluginInstanceConfig>> Host { get; set; }

    IDisposable? Hook;

    public override async Task<IPlugin> Init(IPluginHost<PluginConfigContainer<PluginInstanceConfig>> Host, Action<(string Status, float Percentage)> Progress)
    {
        this.Host = Host;
        Progress(("Initializing...", 0f));

        var notInstalled = new ConcurrentBag<IDaemonService>();
        var outdated = new ConcurrentBag<IDaemonService>();
        var script = DaemonScripts.PeerReputation;
        var scriptHash = SHA256.HashData(Encoding.UTF8.GetBytes(script));
        var scriptId = GUID.ToString();
        var daemons = Host.GetDaemonServices();
        await Task.WhenAll(daemons.Select(async daemon => {
            var installed = (await daemon.ListInstalledScripts()).FirstOrDefault(x => x.Id == scriptId && x.OwnerId == GUID);
            if (installed is null)
                notInstalled.Add(daemon);
            else if (!installed.ScriptHash.AsSpan().SequenceEqual(scriptHash))
                outdated.Add(daemon);
        }));

        await Dispatcher.UIThread.InvokeAsync(async () => {
            if (!notInstalled.IsEmpty || !outdated.IsEmpty) {
                var message = (notInstalled.IsEmpty, outdated.IsEmpty) switch {
                    (false, true) => "The Peer Reputation script is not installed on one or more daemons. Do you want to install it?",
                    (true, false) => "The Peer Reputation script differs from the bundled version on one or more daemons. Do you want to update it?",
                    _ => "The Peer Reputation script is missing or differs from the bundled version on some daemons. Do you want to install or update it?"
                };
                var msgBox = MessageBoxManager.GetMessageBoxStandard("Peer Reputation", message, ButtonEnum.YesNo, Icon.Question);
                if (await msgBox.ShowAsync() == ButtonResult.Yes) {
                    Progress(("Installing or updating script...", 50f));
                    foreach (var daemon in notInstalled.Concat(outdated))
                        await daemon.InstallScript(scriptId, "Peer Reputation", GUID, script, []);
                }
            }

            Hook = this.Host.RegisterToolsMenuItem(new MenuItem() {
                Header = "Peer Reputation",
                Command = new RelayCommand(() => {
                    var wnd = new MainWindow();
                    wnd.ViewModel = new ViewModels.MainWindowViewModel(this.Host.GetDaemonServices(), GUID);
                    wnd.Show();
                })
            });
        });

        Progress(("Done", 100f));

        return this;
    }

    public override Task Unload()
    {
        Hook?.Dispose();

        return Task.CompletedTask;
    }
}
