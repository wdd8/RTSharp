using Avalonia.Controls;

using RTSharp.Scripts;
using RTSharp.Scripts.DuplicateTorrent;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Utils;
using RTSharp.ViewModels;
using RTSharp.Views;

using Serilog;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RTSharp.Core;

public static class TorrentDuplication
{
    public record DuplicationTarget(IDataProvider Provider, string RemoteTargetPath);

    public static async Task<DuplicationTarget?> SelectTarget(Window Owner, Torrent Source, Func<IDataProvider, bool> Filter)
    {
        var vm = new TorrentDuplicationTargetSelectorWindowViewModel(Source);
        vm.Selector.ProviderFilter = Filter;

        var wnd = new TorrentDuplicationTargetSelectorWindow {
            ViewModel = vm
        };

        var result = await wnd.ShowDialog<bool>(Owner);
        if (!result)
            return null;

        var selector = vm.Selector;

        if (selector.SelectedProvider == null || selector.RemoteTargetPath == null)
            return null;

        return new DuplicationTarget(selector.SelectedProvider, selector.RemoteTargetPath);
    }

    public static void AddAndQueue(IReadOnlyList<Torrent> Torrents, IEnumerable<DuplicationTarget> Targets)
    {
        var targets = Targets.ToList();
        if (targets.Count == 0)
            return;

        foreach (var torrent in Torrents) {
            QueueSingle(torrent, targets);
        }
    }

    private static Task<Guid> Start(IReadOnlyList<Torrent> Torrents, IReadOnlyList<DuplicationTarget> Targets, byte[]? TargetTorrent, LinkOptions Links)
    {
        var source = Torrents[0].DataOwner;
        var sourceServerId = source.Host.Config.Plugin.ServerId;

        var input = new DuplicateTorrentInput(
            [.. Torrents.Select(x => x.Hash)],
            source.Host.Config.Plugin.ProviderName,
            source.Host.AttachedDaemonService.Uri.AbsoluteUri,
            [.. Targets.Select(t => new DuplicateTorrentTarget(
                t.Provider.Host.Config.Plugin.ProviderName,
                t.Provider.Host.Config.Plugin.ServerId == sourceServerId ? "" : t.Provider.Host.AttachedDaemonService.Uri.AbsoluteUri,
                t.RemoteTargetPath,
                t.Provider.Capabilities.AddV2OnlyTorrent
            ))],
            TargetTorrent,
            Reflink: Links != LinkOptions.Hardlink,
            Hardlink: Links != LinkOptions.Reflink
        );

        var name = Torrents.Count == 1 ? Torrents[0].Name : $"{Torrents[0].Name} and {Torrents.Count - 1} more";

        return source.Host.AttachedDaemonService.RunCustomScript(DaemonScripts.DuplicateTorrent, $"Duplicate '{name}'", [ScriptMessage.Of(input)]);
    }

    internal static void QueueSingle(Torrent Torrent, IReadOnlyList<DuplicationTarget> Targets)
    {
        _ = Start([ Torrent ], Targets, null, LinkOptions.ReflinkWithHardlinkFallback)
            .ContinueWith(t => {
                if (t.IsFaulted)
                    Log.Logger.Error(t.Exception, $"Duplication: failed to queue script on {Torrent.DataOwner.Host.Config.Plugin.Name}");
            });
    }

    /// <summary>
    /// Duplicates the files of <paramref name="Torrents"/> to the target, but adds and starts another torrent of the
    /// same content there.
    /// </summary>
    /// <param name="Torrents">Torrents of one data provider that together have the content</param>
    /// <param name="TargetTorrent">.torrent of the other torrent</param>
    /// <returns>Info hash of the added torrent</returns>
    public static async Task<byte[]> DuplicateAs(IReadOnlyList<Torrent> Torrents, DuplicationTarget Target, byte[] TargetTorrent, LinkOptions Links)
    {
        var daemon = Torrents[0].DataOwner.Host.AttachedDaemonService;

        var id = await Start(Torrents, [ Target ], TargetTorrent, Links);

        try {
            await using var channel = daemon.OpenScriptChannel(id, default);

            var reply = channel.Read<DuplicateTorrentResult>().AsTask();

            ScriptProgressState? last = null;

            await daemon.GetScriptProgress(id, x => last = x);

            if (last?.State != TASK_STATE.DONE)
                throw new InvalidOperationException(last?.Text ?? "No response from daemon");

            var result = await reply ?? throw new InvalidOperationException("No response from daemon");

            return result.AddedHash;
        } finally {
            await daemon.RemoveScriptSession(id);
        }
    }
}
