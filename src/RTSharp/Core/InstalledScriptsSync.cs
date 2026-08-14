using Avalonia.Controls;
using Avalonia.Threading;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;

using Serilog;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RTSharp.Core;

public static class InstalledScriptsSync
{
    public static async Task CheckOrphans()
    {
        var owners = Plugins.LoadedPlugins
            .Select(x => (x.Instance ?? x.PluginInit as IPlugin)?.GUID)
            .Where(x => x != null)
            .Select(x => x!.Value)
            .ToHashSet();

        owners.Add(Guid.Empty);

        foreach (var (serverId, daemon) in Servers.All) {
            IReadOnlyList<InstalledScript> installed;

            try {
                installed = await daemon.ListInstalledScripts();
            } catch (Exception ex) {
                Log.Logger.Error(ex, $"Could not list installed scripts on {serverId}");
                continue;
            }

            var orphans = installed.Where(x => !x.OrphanAcknowledged && !owners.Contains(x.OwnerId)).ToList();
            if (orphans.Count == 0)
                continue;

            var text = $"There are scripts installed on server {serverId}, without references to running plugin:\n\n" +
                String.Join('\n', orphans.Select(x => $"    {x.Name} ({x.Id})")) +
                "\n\nRemove them from the server?";

            string? answer = null;
            await Dispatcher.UIThread.InvokeAsync(async () => {
                answer = await MessageBoxManager.GetMessageBoxCustom(new() {
                    ContentTitle = "RT# - Orphaned server scripts",
                    ContentMessage = text,
                    ButtonDefinitions = [
                        new MsBox.Avalonia.Models.ButtonDefinition() {
                            Name = "Yes",
                            IsDefault = true
                        },
                        new MsBox.Avalonia.Models.ButtonDefinition() {
                            Name = "No, do not ask again",
                            IsDefault = false
                        }
                    ],
                    Icon = Icon.Question,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }).ShowWindowDialogAsync(App.MainWindow);
            });

            foreach (var orphan in orphans) {
                try {
                    if (answer == "Yes")
                        await daemon.UninstallScript(orphan.Id);
                    else
                        await daemon.AcknowledgeOrphanedScript(orphan.Id);
                } catch (Exception ex) {
                    Log.Logger.Error(ex, $"Could not resolve orphaned script {orphan.Id} on {serverId}");
                }
            }
        }
    }
}
