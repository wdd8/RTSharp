using RTSharp.Scripts;
using RTSharp.Scripts.DuplicateTorrent;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Utils;

using Serilog;

using System;
using System.Collections.Generic;
using System.Linq;

namespace RTSharp.Core;

public static class TorrentDuplication
{
    public record DuplicationTarget(IDataProvider Provider, string RemoteTargetPath);

    public static void AddAndQueue(IReadOnlyList<Torrent> Torrents, IEnumerable<DuplicationTarget> Targets)
    {
        var targets = Targets.ToList();
        if (targets.Count == 0)
            return;

        foreach (var torrent in Torrents) {
            QueueSingle(torrent, targets);
        }
    }

    internal static void QueueSingle(Torrent Torrent, IReadOnlyList<DuplicationTarget> Targets)
    {
        var source = Torrent.DataOwner;
        var sourceServerId = source.Host.Config.Plugin.ServerId;

        var input = new DuplicateTorrentInput(
            Torrent.Hash,
            source.Host.Config.Plugin.ProviderName,
            source.Host.AttachedDaemonService.Uri.AbsoluteUri,
            [.. Targets.Select(t => new DuplicateTorrentTarget(
                t.Provider.Host.Config.Plugin.ProviderName,
                t.Provider.Host.Config.Plugin.ServerId == sourceServerId ? "" : t.Provider.Host.AttachedDaemonService.Uri.AbsoluteUri,
                t.RemoteTargetPath
            ))]
        );

        _ = source.Host.AttachedDaemonService.RunCustomScript(DaemonScripts.DuplicateTorrent, $"Duplicate '{Torrent.Name}'", [ScriptMessage.Of(input)])
            .ContinueWith(t => {
                if (t.IsFaulted)
                    Log.Logger.Error(t.Exception, $"Duplication: failed to queue script on {source.Host.Config.Plugin.Name}");
            });
    }
}
