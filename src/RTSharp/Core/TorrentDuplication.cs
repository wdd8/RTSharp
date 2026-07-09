using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;

using Serilog;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RTSharp.Core;

public static class TorrentDuplication
{
    public record DuplicationTarget(IDataProvider Provider, string RemoteTargetPath);

    public static async Task AddAndQueue(IReadOnlyList<(Torrent Torrent, byte[] TorrentBytes)> Torrents, IEnumerable<DuplicationTarget> Targets)
    {
        foreach (var target in Targets) {
            try {
                await target.Provider.AddTorrents([.. Torrents.Select(x => (
                    Data: x.TorrentBytes,
                    Filename: (string?)null,
                    Options: new AddTorrentsOptions(null, target.RemoteTargetPath)
                ))]);

                foreach (var (torrent, bytes) in Torrents) {
                    QueueSingle(torrent, target.Provider, target.RemoteTargetPath);
                }
            } catch (Exception ex) {
                Log.Logger.Error(ex, $"Duplication: failed to queue duplication to {target.Provider.Host.Config.Plugin.Name}");
            }
        }
    }

    internal static void QueueSingle(Torrent Torrent, IDataProvider Target, string TargetPath)
    {
        Uri? sourceUri = null, targetUri = null;

        var source = Torrent.DataOwner;

        if (source.Host.Config.Plugin.ServerId != Target.Host.Config.Plugin.ServerId) {
            targetUri = Target.Host.AttachedDaemonService.Uri;
            sourceUri = source.Host.AttachedDaemonService.Uri;
        }

        var variables = new Dictionary<string, string> {
            ["Hash"] = Convert.ToHexString(Torrent.Hash),
            ["SourceProvider"] = source.Host.Config.Plugin.ProviderName,
            ["TargetProvider"] = Target.Host.Config.Plugin.ProviderName,
            ["TargetUri"] = targetUri?.AbsoluteUri ?? "",
            ["SourceUri"] = sourceUri?.AbsoluteUri ?? "",
            ["TargetPath"] = TargetPath
        };

        _ = source.Host.AttachedDaemonService.RunCustomScript(Script, $"Duplicate '{Torrent.Name}'", variables)
            .ContinueWith(t => {
                if (t.IsFaulted)
                    Log.Logger.Error(t.Exception, $"Duplication: failed to queue script on {source.Host.Config.Plugin.Name}");
            });
    }

    const string Script =
"""
#pragma usings
#pragma lib "Grpc.Core.Api"
#pragma lib "Google.Protobuf"

using RTSharp.Daemon.Primitives;
using RTSharp.Daemon.GRPCServices.DataProvider;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Services;
using RTSharp.Shared.Utils;
using System.Linq;

public class Main(RegisteredDataProviders DataProviders, RTSharp.Daemon.Services.TorrentOpService TorrentOps) : IScript
{
    public async Task Execute(Dictionary<string, string> Variables, IScriptSession Session, CancellationToken CancellationToken)
    {
        Session.Progress.State = TASK_STATE.RUNNING;
        Session.Progress.Text = "Waiting for source torrent to complete...";

        var hash = Convert.FromHexString(Variables["Hash"]);
        var sourceDp = DataProviders.GetDataProvider(Variables["SourceProvider"]) ?? throw new InvalidOperationException($"Unknown source data provider '{Variables["SourceProvider"]}'");

        await TorrentOps.WaitForCompletion(sourceDp, hash, CancellationToken);

        if (!string.IsNullOrEmpty(Variables["TargetUri"])) {
            Session.Progress.Text = "Transmitting...";
            var target = new DataProviderEndpoint(Variables["TargetUri"], Variables["TargetProvider"]);
            await TorrentOps.TransmitTorrent(sourceDp, hash, target, Variables["SourceUri"], Variables["TargetPath"], CancellationToken);
        }

        Session.Progress.Text = "Rechecking...";
        // TODO: use fast-resume
        if (!string.IsNullOrEmpty(Variables["TargetUri"])) {
            var target = new DataProviderEndpoint(Variables["TargetUri"], Variables["TargetProvider"]);
            await TorrentOps.Remote.ForceRecheckTorrents(target, hash, Session.Progress, CancellationToken);
            await TorrentOps.Remote.TryStartSeeding(target, hash, CancellationToken);
        } else {
            var targetDp = DataProviders.GetDataProvider(Variables["TargetProvider"]) ?? throw new InvalidOperationException($"Unknown target data provider: {Variables["TargetProvider"]}");
            var torrents = new Torrents { Hashes = { RTSharp.Shared.Utils.GrpcExtensions.ToByteString(hash) } };
            await TorrentOps.ForceRecheckTorrents(targetDp, torrents, Session.Progress, CancellationToken);
            await TorrentOps.TryStartSeeding(targetDp, torrents, CancellationToken);
        }

        Session.Progress.Text = "Done";
        Session.Progress.State = TASK_STATE.DONE;
    }
}
""";
}
