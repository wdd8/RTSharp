#pragma usings

using RTSharp.Daemon.Primitives;
using RTSharp.Daemon.GRPCServices.DataProvider;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Services;
using RTSharp.Shared.Utils;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RTSharp.Scripts.DuplicateTorrent;

public class Main(RegisteredDataProviders DataProviders, RTSharp.Daemon.Services.TorrentOpService TorrentOps) : IScript
{
    record DuplicationScriptTarget(string Provider, string? Uri, string Path);

    public async Task Execute(Dictionary<string, string> Variables, IScriptSession Session, CancellationToken CancellationToken)
    {
        Session.Progress.State = TASK_STATE.RUNNING;
        Session.Progress.Text = "Waiting for source torrent to complete...";

        var hash = Convert.FromHexString(Variables["Hash"]);
        var sourceUri = Variables["SourceUri"];
        var sourceDp = DataProviders.GetDataProvider(Variables["SourceProvider"]) ?? throw new InvalidOperationException($"Unknown source data provider '{Variables["SourceProvider"]}'");
        var targets = JsonSerializer.Deserialize<DuplicationScriptTarget[]>(Variables["Targets"]) ?? Array.Empty<DuplicationScriptTarget>();

        await TorrentOps.WaitForCompletion(sourceDp, hash, CancellationToken);

        var dotTorrentMs = new MemoryStream();
        var getDotTorrentTask = Task.Run(async () => {
            using (var stream = await TorrentOps.GetDotTorrentStream(sourceDp, hash, CancellationToken)) {
                await stream.CopyToAsync(dotTorrentMs, CancellationToken);
                dotTorrentMs.Position = 0;
            }
        });

        foreach (var server in targets.GroupBy(t => t.Uri)) {
            var paths = server.Select(t => t.Path).Distinct().ToList();

            if (String.IsNullOrEmpty(server.Key)) {
                Session.Progress.Text = "Linking files...";
                foreach (var path in paths)
                    await TorrentOps.LinkFiles(sourceDp, hash, path, CancellationToken);
            } else {
                var endpoint = new DataProviderEndpoint(server.Key, server.First().Provider);

                var initialPath = paths[0];
                Session.Progress.Text = "Transmitting...";
                await TorrentOps.TransmitTorrent(sourceDp, hash, endpoint, sourceUri, initialPath, CancellationToken);

                if (paths.Count > 1) {
                    Session.Progress.Text = "Linking files...";
                    var source = await TorrentOps.GetTorrent(sourceDp, hash);
                    var filesReply = await TorrentOps.GetTorrentsFiles(sourceDp, new Torrents { Hashes = { hash.ToByteString() } });
                    var files = filesReply.Reply.FirstOrDefault(x => x.InfoHash.ToByteArray().SequenceEqual(hash)) ?? throw new InvalidOperationException($"No file listing returned for hash {Convert.ToHexString(hash)}");

                    foreach (var path in paths.Skip(1))
                        await TorrentOps.Remote.LinkFiles(endpoint, initialPath, path, source.Name, files, CancellationToken);
                }
            }
        }

        Session.Progress.Text = "Adding torrents...";
        await getDotTorrentTask;
        foreach (var target in targets) {
            if (String.IsNullOrEmpty(target.Uri)) {
                var targetDp = DataProviders.GetDataProvider(target.Provider) ?? throw new InvalidOperationException($"Unknown target data provider: {target.Provider}");
                await TorrentOps.AddTorrent(targetDp, dotTorrentMs, target.Path, "", true);
            } else {
                var endpoint = new DataProviderEndpoint(target.Uri, target.Provider);
                await TorrentOps.Remote.AddTorrent(endpoint, dotTorrentMs, target.Path, "", true, CancellationToken);
            }
            dotTorrentMs.Position = 0;
        }
        dotTorrentMs.Dispose();

        Session.Progress.Text = "Starting...";
        foreach (var target in targets) {
            if (String.IsNullOrEmpty(target.Uri)) {
                var targetDp = DataProviders.GetDataProvider(target.Provider) ?? throw new InvalidOperationException($"Unknown target data provider: {target.Provider}");
                await TorrentOps.TryStartSeeding(targetDp, new Torrents { Hashes = { hash.ToByteString() } }, CancellationToken);
            } else {
                var endpoint = new DataProviderEndpoint(target.Uri, target.Provider);
                await TorrentOps.Remote.TryStartSeeding(endpoint, hash, CancellationToken);
            }
        }

        Session.Progress.Text = "Done";
        Session.Progress.State = TASK_STATE.DONE;
    }
}
