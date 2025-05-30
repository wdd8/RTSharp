using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Daemon;
using File = System.IO.File;

namespace RTSharp.Core
{
    public class TorrentAdd
    {
        public enum START_MODE
        {
            DO_NOTHING,
            START,
            RECHECK_AND_START
        }

        public record struct TorrentAddInput(
            IList<(string Source, Shared.Abstractions.AddTorrentsOptions Options)> Sources,
            IEnumerable<DataProvider> DuplicationTargets,
            START_MODE StartMode);

        public static async Task AddTorrents(DataProvider Primary, TorrentAddInput Input)
        {
            var tasks = new List<Task<(string? Filename, byte[] Content)>>();

            foreach (var (source, _) in Input.Sources) {
                Func<Task<(string? Filename, byte[] Content)>> fx;

                if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && !uri.IsFile) {
                    fx = async () => {
                        var req = await Core.Http.Client.GetAsync(uri);
                        var content = await req.Content.ReadAsByteArrayAsync();
                        return (req.Content.Headers.ContentDisposition?.FileNameStar ?? Regex.Replace(req.Content.Headers.ContentDisposition?.FileName ?? "", "^\\\"(.*)\\\"$", "$1"), content);
                    };
                } else
                    fx = async () => (Path.GetFileName(source), await File.ReadAllBytesAsync(source));

                tasks.Add(fx());
            }

            var files = (await Task.WhenAll(tasks)).Select((data, x) => (data.Content, data.Filename, Input.Sources[x].Options)).ToList();

            var action = ActionQueueAction.New("Add torrents", () => {
                return Primary.Instance.AddTorrents(files);
            });
            if (Primary.Instance.Capabilities.ForceStartTorrentOnAdd == null) {
                switch (Input.StartMode) {
                    case START_MODE.DO_NOTHING:
                        break;
                    case START_MODE.START:
                        action.CreateChild("Start torrents", RUN_MODE.DEPENDS_ON_PARENT, parent => {
                            var res = parent.GetResult()!;
                            foreach (var (hash, exceptions) in res) {
                                if (exceptions.All(x => x == null))
                                    Primary.PluginInstance.Logger.Information($"Torrent {Convert.ToHexString(hash)} added");
                                else
                                    Primary.PluginInstance.Logger.Error($"{Convert.ToHexString(hash)}: {String.Join("\n", exceptions.Select(x => x.Message))}");
                            }

                            return Primary.Instance.StartTorrents(res.Where(x => x.Exceptions.All(i => i == null)).Select(x => x.Hash).ToArray());
                        }).CreateChild("Start torrent completion", RUN_MODE.DEPENDS_ON_PARENT, startTorrentAction => {
                            foreach (var (hash, exceptions) in startTorrentAction.GetResult()!) {
                                if (exceptions.All(x => x == null))
                                    Primary.PluginInstance.Logger.Information($"Torrent {Convert.ToHexString(hash)} started");
                                else
                                    Primary.PluginInstance.Logger.Error($"{Convert.ToHexString(hash)}: {String.Join("\n", exceptions.Select(x => x.Message))}");
                            }

                            return default;
                        });
                        break;
                    case START_MODE.RECHECK_AND_START:
                        action.CreateChild("Recheck", RUN_MODE.DEPENDS_ON_PARENT, async parent => {
                            var torrentHashes = parent.GetResult()!.Where(x => x.Exceptions.All(i => i == null)).Select(x => x.Hash).ToArray();

                            var guid = await Primary.Instance.ForceRecheck(torrentHashes);
                            
                            await Primary.PluginInstance.AttachedDaemonService.GetScriptProgress(guid, null);
                            
                            // TODO: report proper status
                            return new TorrentStatuses(torrentHashes.Select(x => (x, (IList<Exception>)Array.Empty<Exception>())));
                        }).CreateChild("Start torrents", RUN_MODE.DEPENDS_ON_PARENT, recheckAction => {
                            var recheckResults = recheckAction.GetResult()!.Where(x => x.Exceptions.All(i => i != null));

                            return Primary.Instance.StartTorrents(recheckResults.Select(x => x.Hash).ToArray());
                        });
                        break;
                }
            }

            var queue = Core.ActionQueue.GetActionQueueEntry(Primary.PluginInstance);
            var response = await queue.Queue.RunAction(action);

            foreach (var torrent in response) {
                if (torrent.Exceptions.Any(x => x != null))
                    Primary.PluginInstance.Logger.Error($"{Convert.ToHexString(torrent.Hash)}: {String.Join("; ", torrent.Exceptions.Select(x => x.Message))}");
            }
        }
    }
}
