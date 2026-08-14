using RTSharp.Scripts;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;

using Serilog;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RTSharp.Core;

internal record DuplicationScriptTarget(string Provider, string? Uri, string Path);

[JsonSerializable(typeof(List<DuplicationScriptTarget>))]
internal partial class DuplicationScriptTargetContext : JsonSerializerContext { }

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

        var scriptTargets = Targets.Select(t => new DuplicationScriptTarget(
            t.Provider.Host.Config.Plugin.ProviderName,
            t.Provider.Host.Config.Plugin.ServerId == sourceServerId ? "" : t.Provider.Host.AttachedDaemonService.Uri.AbsoluteUri,
            t.RemoteTargetPath
        )).ToList();

        var variables = new Dictionary<string, string> {
            ["Hash"] = Convert.ToHexString(Torrent.Hash),
            ["SourceProvider"] = source.Host.Config.Plugin.ProviderName,
            ["SourceUri"] = source.Host.AttachedDaemonService.Uri.AbsoluteUri,
            ["Targets"] = JsonSerializer.Serialize(scriptTargets, DuplicationScriptTargetContext.Default.ListDuplicationScriptTarget)
        };

        _ = source.Host.AttachedDaemonService.RunCustomScript(DaemonScripts.DuplicateTorrent, $"Duplicate '{Torrent.Name}'", variables)
            .ContinueWith(t => {
                if (t.IsFaulted)
                    Log.Logger.Error(t.Exception, $"Duplication: failed to queue script on {source.Host.Config.Plugin.Name}");
            });
    }
}
