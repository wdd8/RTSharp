using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.Input;

using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;

using RTSharp.Scripts;
using RTSharp.Scripts.FastVerify;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Utils;

namespace RTSharp.FastVerify.Plugin;

public class Plugin : BasePlugin<PluginConfigContainer<PluginInstanceConfig>>
{
    public override string Name => "Fast verify";

    public override string Description => "Adds a torrent context menu item that fast-verifies files on disk against random pieces";

    public override string Author => "RTSharp";

    public override int CompatibleMajorVersion => 0;

    public override Guid GUID { get; } = new Guid("F62FC527-E49D-4DAB-B863-EEDC880F27E1");

    public override PluginCapabilities Capabilities => new PluginCapabilities(
        HasSettingsWindow: false
    );

    public required IPluginHost<PluginConfigContainer<PluginInstanceConfig>> Host { get; set; }

    IDisposable? MenuItem;

    const string MenuHeader = "Fast verify";

    public override Task<IPlugin> Init(IPluginHost<PluginConfigContainer<PluginInstanceConfig>> Host, Action<(string Status, float Percentage)> Progress)
    {
        this.Host = Host;
        Progress(("Initializing...", 0f));

        Dispatcher.UIThread.Invoke(() => {
            MenuItem = this.Host.RegisterTorrentContextMenuItem(list => {
                var newMenuItem = new MenuItem {
                    Header = MenuHeader,
                    Command = new AsyncRelayCommand<IReadOnlyList<Torrent>>(selected => Do(selected!))
                };

                list.Add(newMenuItem);
                return newMenuItem;
            }, list => {
                for (var x = 0; x < list.Count; x++) {
                    if (list[x] is MenuItem menuItem && menuItem.Header!.ToString() == MenuHeader) {
                        list.RemoveAt(x);
                    }
                }
            });
        });

        Progress(("Done", 100f));

        return Task.FromResult((IPlugin)this);
    }

    public async Task Do(IReadOnlyList<Torrent> In)
    {
        if (In.Count == 0)
            return;

        var results = await Task.WhenAll(In.Select(async torrent => {
            try {
                var (success, detail) = await Verify(torrent);
                return (torrent.Name, Success: success, Detail: detail);
            } catch (Exception ex) {
                Host.Logger.Error(ex, "Fast verify failed for {Torrent}", torrent.Name);
                return (torrent.Name, Success: false, Detail: (string?)$"Verification could not complete: {ex.Message}");
            }
        }));

        string message;
        Icon icon;

        if (results.Length == 1) {
            var (name, success, detail) = results[0];
            message = $"{name}\n\n{(success ? "Verified OK" : "Verification failed")}{(detail == null ? "" : $"\n{detail} ")}";
            icon = success ? Icon.Success : Icon.Error;
        } else {
            message = string.Join("\n\n", results.Select(r =>
                $"{r.Name}\n{(r.Success ? "Verified OK" : "Verification failed")}{(r.Detail == null ? "" : $": {r.Detail}")}"));

            var successCount = results.Count(r => r.Success);
            icon = successCount == results.Length ? Icon.Success : successCount == 0 ? Icon.Error : Icon.Warning;
        }

        var msgbox = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams {
            ButtonDefinitions = ButtonEnum.Ok,
            ContentTitle = "RT# - Fast verify",
            ContentMessage = message,
            Icon = icon,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        });

        await Dispatcher.UIThread.InvokeAsync(async () => {
            await msgbox.ShowWindowDialogAsync(Host.MainWindow);
        });
    }

    private async Task<(bool Success, string? Detail)> Verify(Torrent torrent)
    {
        var daemon = torrent.DataOwner.Host.AttachedDaemonService;

        var dotTorrentBytes = (await torrent.DataOwner.GetDotTorrents([torrent])).Single().Value;

        var scriptId = await daemon.RunCustomScript(DaemonScripts.FastVerify, "FastVerify",
            [ScriptMessage.Of(new FastVerifyInput(dotTorrentBytes, torrent.RemotePath))]);

        try {
            await using var channel = daemon.OpenScriptChannel(scriptId, CancellationToken.None);

            var result = await channel.Read<FastVerifyResult>();

            if (result == null)
                return (false, "No response from daemon");

            return (result.Verified, result.Detail ?? (result.Verified ? null : "Verification failed"));
        } finally {
            await daemon.RemoveScriptSession(scriptId);
        }
    }

    public override Task Unload()
    {
        MenuItem?.Dispose();

        return Task.CompletedTask;
    }
}
