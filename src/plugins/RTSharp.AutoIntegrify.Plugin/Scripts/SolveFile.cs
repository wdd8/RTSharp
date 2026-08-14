#pragma usings

using System.Text;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RTSharp.Scripts.SolveFile;

public class Main(ILogger<Main> Logger) : IScript
{
    public async Task Execute(Dictionary<string, string> Variables, IScriptSession Session, CancellationToken CancellationToken)
    {
        Session.Progress.State = TASK_STATE.RUNNING;
        Session.Progress.Text = "Searching...";

        var addPaths = Variables["AdditionalPaths"].Split('\x00');
        var current = Variables["CurrentPath"];
        var filePath = Variables["FilePath"];
        var size = Int64.Parse(Variables["Size"]);
        var isMultiFile = Variables["IsMultiFile"] == "1";
        var wideSearch = Variables["WideSearch"] == "1";
        var recurseLimit = Int32.Parse(Variables["RecurseLimit"]);

        // technically not torrent name because it would lack extension on single file torrents but it's more convenient this way
        var torrentName = isMultiFile ? current.Split('/').Last() : Path.GetFileNameWithoutExtension(filePath);

        var outp = new StringBuilder();
        void append(string inp)
        {
            outp.Append(inp);
            outp.Append('\x00');
        }

        var progressStopwatch = Stopwatch.StartNew();

        bool matchFolder(string path, int recursed = 0) {
            if (recursed >= recurseLimit)
                return false;

            try {
                var currentDir = new DirectoryInfo(path);

                if (progressStopwatch.ElapsedMilliseconds >= 1000) {
                    Session.Progress.Text = path;
                    progressStopwatch.Restart();
                }

                foreach (var file in currentDir.EnumerateFiles()) {
                    if (file.FullName.EndsWith(filePath)) {
                        append(file.FullName);
                        if (size == file.Length) {
                            // Strong match, signal outside to bail
                            return true;
                        }
                        continue;
                    }
                    if (size == file.Length) {
                        append(file.FullName);
                    }
                }
                foreach (var dir in currentDir.EnumerateDirectories()) {
                    if (matchFolder(dir.FullName, recursed + 1)) {
                        return true;
                    }
                }
            } catch {
                Logger.LogWarning($"AutoIntegrify: Cannot query directory {path}");
            }

            return false;
        }

        // First try exact matches in current folder
        if (matchFolder(current)) {
            goto done;
        }

        foreach (var path in addPaths) {
            try {
                var dir = new DirectoryInfo(path);
                foreach (var info in dir.EnumerateFileSystemInfos()) {
                    if (info.Attributes.HasFlag(FileAttributes.Directory)) {
                        if (info.Name == torrentName || wideSearch) {
                            Logger.LogTrace($"Considering {info.FullName}...");
                            if (matchFolder(info.FullName)) {
                                // Got a strong match, I don't think we will need other folders...
                                goto done;
                            }
                        }
                    } else {
                        if (info.FullName.EndsWith(filePath) || wideSearch) {
                            try {
                                var thisFile = new FileInfo(info.FullName);
                                if (thisFile.Length == size) {
                                    append(info.FullName);
                                    continue;
                                }
                            } catch {
                                Logger.LogWarning($"AutoIntegrify: Cannot query file {info.FullName}");
                            }
                        }
                    }
                }
            } catch {
                Logger.LogWarning($"AutoIntegrify: Cannot query directory {path}");
            }
        }

done:;
        Session.Progress.StateData = outp.ToString().TrimEnd('\x00');
        Session.Progress.State = TASK_STATE.DONE;
    }
}
