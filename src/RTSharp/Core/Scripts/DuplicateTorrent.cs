#pragma usings
#pragma contract DuplicateTorrentMessages

using BencodeNET.Objects;
using BencodeNET.Parsing;
using RTSharp.Daemon.Primitives;
using RTSharp.Daemon.GRPCServices.DataProvider;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Services;
using RTSharp.Shared.Utils;
using System.IO;
using System.Linq;
using LinkType = RTSharp.Daemon.Protocols.LinkFilesInput.Types.LinkType;

namespace RTSharp.Scripts.DuplicateTorrent;

public class Main(RegisteredDataProviders DataProviders, RTSharp.Daemon.Services.TorrentOpService TorrentOps) : IScript
{
    public async Task Execute(IScriptSession Session, CancellationToken CancellationToken)
    {
        Session.Progress.State = TASK_STATE.RUNNING;
        Session.Progress.Text = "Waiting for source torrent to complete...";

        var input = await Session.Rx<DuplicateTorrentInput>(CancellationToken);

        try {
            await Duplicate(Session, input, CancellationToken);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            Session.Progress.Text = ex.Message;
            Session.Progress.State = TASK_STATE.FAILED;
            throw;
        }

        Session.Progress.Text = "Done";
        Session.Progress.State = TASK_STATE.DONE;
    }

    private async Task Duplicate(IScriptSession Session, DuplicateTorrentInput input, CancellationToken CancellationToken)
    {
        var hash = input.Hashes[0];
        var sourceUri = input.SourceUri;
        var sourceDp = DataProviders.GetDataProvider(input.SourceProvider) ?? throw new InvalidOperationException($"Unknown source data provider '{input.SourceProvider}'");
        var targets = input.Targets;

        foreach (var source in input.Hashes)
            await TorrentOps.WaitForCompletion(sourceDp, source, CancellationToken);

        var dotTorrentMs = new MemoryStream();
        if (input.TargetTorrent != null) {
            dotTorrentMs.Write(input.TargetTorrent);
        } else {
            using var stream = await TorrentOps.GetDotTorrentStream(sourceDp, hash, CancellationToken);
            await stream.CopyToAsync(dotTorrentMs, CancellationToken);
        }
        dotTorrentMs.Position = 0;

        // Before any files end up at a target that can't add the torrent
        var (v1, _) = BencodeUtils.GetInfoHashes(new BencodeParser().Parse<BDictionary>(dotTorrentMs));
        dotTorrentMs.Position = 0;

        var unsupported = targets.FirstOrDefault(x => !x.AddsV2OnlyTorrents);
        if (v1 == null && unsupported != null)
            throw new InvalidOperationException($"{unsupported.Provider} does not support v2-only torrents");

        var files = await TorrentOps.GetFilePlacements(sourceDp, input.Hashes, input.TargetTorrent, CancellationToken);
        var linkType = (input.Reflink ? LinkType.Reflink : 0) | (input.Hardlink ? LinkType.Hardlink : 0);

        foreach (var server in targets.GroupBy(t => t.Uri)) {
            var paths = server.Select(t => t.Path).Distinct().ToList();

            if (String.IsNullOrEmpty(server.Key)) {
                Session.Progress.Text = "Linking files...";

                var linking = new ScriptProgressState(Session) { Text = "Linking files...", Progress = 0f, State = TASK_STATE.RUNNING };
                Session.Progress.Chain = [ linking ];

                foreach (var path in paths)
                    await TorrentOps.LinkFiles(path, linkType, files, linking, CancellationToken);

                linking.State = TASK_STATE.DONE;
            } else {
                var endpoint = new DataProviderEndpoint(server.Key, server.First().Provider);

                var initialPath = paths[0];
                Session.Progress.Text = "Transmitting...";
                await TorrentOps.TransmitTorrent(hash, endpoint, sourceUri, initialPath, files, CancellationToken);

                if (paths.Count > 1) {
                    Session.Progress.Text = "Linking files...";

                    foreach (var path in paths.Skip(1))
                        await TorrentOps.Remote.LinkFiles(endpoint, initialPath, path, linkType, files, CancellationToken);
                }
            }
        }

        Session.Progress.Text = "Adding torrents...";

        var addedHashes = new byte[targets.Length][];
        for (var x = 0;x < targets.Length;x++) {
            var target = targets[x];

            TorrentsReply reply;
            if (String.IsNullOrEmpty(target.Uri)) {
                var targetDp = DataProviders.GetDataProvider(target.Provider) ?? throw new InvalidOperationException($"Unknown target data provider: {target.Provider}");
                reply = await TorrentOps.AddTorrent(targetDp, dotTorrentMs, target.Path, "", true);
            } else {
                var endpoint = new DataProviderEndpoint(target.Uri, target.Provider);
                reply = await TorrentOps.Remote.AddTorrent(endpoint, dotTorrentMs, target.Path, "", true, CancellationToken);
            }
            dotTorrentMs.Position = 0;

            var added = reply.Torrents.Single();

            var failure = added.Status.FirstOrDefault(i => i.FaultCode is not (null or "0"));
            if (failure != null)
                throw new InvalidOperationException($"{failure.Command}: Code {failure.FaultCode} - {failure.FaultString}");

            addedHashes[x] = added.InfoHash.ToByteArray();
        }
        dotTorrentMs.Dispose();

        Session.Progress.Text = "Starting...";
        for (var x = 0;x < targets.Length;x++) {
            var target = targets[x];

            if (String.IsNullOrEmpty(target.Uri)) {
                var targetDp = DataProviders.GetDataProvider(target.Provider) ?? throw new InvalidOperationException($"Unknown target data provider: {target.Provider}");
                await TorrentOps.TryStartSeeding(targetDp, new Torrents { Hashes = { addedHashes[x].ToByteString() } }, CancellationToken);
            } else {
                var endpoint = new DataProviderEndpoint(target.Uri, target.Provider);
                await TorrentOps.Remote.TryStartSeeding(endpoint, addedHashes[x], CancellationToken);
            }
        }

        Session.Send(new DuplicateTorrentResult(addedHashes[0]));
    }
}
