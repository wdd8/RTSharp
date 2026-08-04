using BencodeNET.Parsing;

using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Utils;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

using File = System.IO.File;

namespace RTSharp.Core;

public class TorrentAdd
{
    public enum START_MODE
    {
        DO_NOTHING,
        START,
        RECHECK_AND_START
    }

    public record struct TorrentAddInput(
        IList<(string Source, AddTorrentsOptions Options)> Sources,
        IEnumerable<TorrentDuplication.DuplicationTarget> DuplicationTargets,
        START_MODE StartMode);

    public static async Task<IList<Torrent>> AddTorrents(IDataProvider Primary, TorrentAddInput Input)
    {
        var plugin = Plugins.GetPluginOrDefault(Primary) ?? throw new NullReferenceException("No plugin corresponding to data provider found");

        var tasks = new List<Task<(string? Filename, byte[]? Content, string? MagnetUri)>>();

        foreach (var (source, opts) in Input.Sources) {
            Func<Task<(string? Filename, byte[]? Content, string? MagnetUri)>> fx;

            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && !uri.IsFile) {
                if (uri.Scheme == "magnet") {
                    if (opts.FastResume) {
                        throw new InvalidOperationException("Cannot add magnet torrent with fastresume");
                    }

                    fx = async () => {
                        if (!MagnetParser.TryParse(uri.OriginalString, out var magnet)) {
                            throw new InvalidOperationException("Invalid magnet URI");
                        }

                        return (magnet.DisplayName ?? null, null, uri.OriginalString);
                    };
                } else {
                    fx = async () => {
                        var req = await Core.Http.Client.GetAsync(uri);
                        var content = await req.Content.ReadAsByteArrayAsync();
                        return (req.Content.Headers.ContentDisposition?.FileNameStar ?? Regex.Replace(req.Content.Headers.ContentDisposition?.FileName ?? "", "^\\\"(.*)\\\"$", "$1"), content, null);
                    };
                }
            } else
                fx = async () => (Path.GetFileName(source), await File.ReadAllBytesAsync(source), null);

            tasks.Add(fx());
        }

        var files = (await Task.WhenAll(tasks)).Select((data, x) => (data.Content, data.MagnetUri, data.Filename, Input.Sources[x].Options)).ToList();

        var filesByHash = new InfoHashDictionary<(byte[]? Content, string? MagnetUri, string Name, AddTorrentsOptions Options)>();
        {
            var bencodeParser = new BencodeParser();
            foreach (var (content, magnetUri, _, options) in files) {
                if (magnetUri != null)
                    continue;

                try {
                    using var ms = new MemoryStream(content!);
                    var bTorrent = bencodeParser.Parse<BencodeNET.Objects.BDictionary>(ms);
                    var (v1, v2) = BencodeUtils.GetInfoHashes(bTorrent);
                    var name = BencodeUtils.GetName(bTorrent.Get<BencodeNET.Objects.BDictionary>("info"));

                    filesByHash[(v2 ?? v1)!] = (content, null, name, options);
                } catch (Exception ex) {
                    plugin.Logger.Error(ex, "Failed to parse a source .torrent for correlation");
                }
            }
        }

        ActionQueueAction<TorrentStatuses> parentAction;
        var action = parentAction = ActionQueueAction.New<TorrentStatuses>("Add torrents", (action) => {
            return Primary.AddTorrents(files);
        });
        if (Primary.Capabilities.ForceStartTorrentOnAdd == null) {
            switch (Input.StartMode) {
                case START_MODE.DO_NOTHING:
                    break;
                case START_MODE.START:
                    action = action.CreateChild<TorrentStatuses>("Start torrents", RUN_MODE.DEPENDS_ON_PARENT, async (parent, action) => {
                        var res = parent.GetResult()!;
                        foreach (var (hash, exceptions) in res) {
                            if (exceptions.All(x => x == null))
                                plugin.Logger.Information($"Torrent {Convert.ToHexString(hash)} added");
                            else
                                plugin.Logger.Error($"{Convert.ToHexString(hash)}: {String.Join("\n", exceptions.Select(x => x.Message))}");
                        }

                        var ret = await Primary.StartTorrents([.. res.Where(x => x.Exceptions.All(i => i == null)).Select(x => x.Hash)]);

                        return ret;
                    });
                    break;
                case START_MODE.RECHECK_AND_START:
                    action = action.CreateChild<TorrentStatuses>("Recheck", RUN_MODE.DEPENDS_ON_PARENT, async (parent, action) => {
                        var torrentHashes = parent.GetResult()!.Where(x => x.Exceptions.All(i => i == null)).Select(x => x.Hash).ToArray();

                        var guid = await Primary.ForceRecheck(torrentHashes);
                        
                        await Primary.Host.AttachedDaemonService.GetScriptProgress(guid, null);
                        
                        // TODO: report proper status
                        return new TorrentStatuses(torrentHashes.Select(x => (x, (IList<Exception>)Array.Empty<Exception>())));
                    }).CreateChild<TorrentStatuses>("Start torrents", RUN_MODE.DEPENDS_ON_PARENT, (recheckAction, action) => {
                        var recheckResults = recheckAction.GetResult()!.Where(x => x.Exceptions.All(i => i != null));

                        return Primary.StartTorrents(recheckResults.Select(x => x.Hash).ToArray());
                    });
                    break;
            }
        }

        var getAddedTorrents = action.CreateChild<IList<Torrent>>("Get added torrents", RUN_MODE.DEPENDS_ON_PARENT, async (parent, action) => {
            var addedHashes = parent.GetResult()!.Where(x => x.Exceptions.All(e => e == null)).Select(x => x.Hash);
            var torrents = await Task.WhenAll(addedHashes.Select(Primary.GetTorrent));

            foreach (var torrent in torrents) {
                if (filesByHash.TryGetValue(torrent.Hash, out var source) && source.Options.FastResume && !torrent.IsCompleted) {
                    plugin.Logger.Warning($"Fast resume for {Convert.ToHexString(torrent.Hash)} failed");
                }
            }

            return torrents;
        });

        if (Input.DuplicationTargets.Any()) {
            getAddedTorrents.CreateChild("Duplicate", RUN_MODE.DEPENDS_ON_PARENT, async (parent, action) => {
                var successfulAdds = parent.GetResult()!
                    .Where(x => filesByHash.ContainsKey(x.Hash))
                    .ToList();

                if (successfulAdds.Count > 0)
                    TorrentDuplication.AddAndQueue(successfulAdds, Input.DuplicationTargets);
            });
        }

        var queue = ActionQueue.GetActionQueueEntry(Plugins.GetPluginOrDefault(Primary));
        if (queue == null)
            return [];

        await queue.Queue.RunAction(parentAction);
        await parentAction.WaitForCompletion();

        return getAddedTorrents.GetResult()!;
    }
}
