#pragma usings
#pragma contract SolveFileMessages

using System.Linq;
using System.IO;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RTSharp.Scripts.SolveFile;

public class Main(ILogger<Main> Logger) : IScript
{
    public async Task Execute(IScriptSession Session, CancellationToken CancellationToken)
    {
        Session.Progress.State = TASK_STATE.RUNNING;
        Session.Progress.Text = "Searching...";

        var input = await Session.Rx<SolveFileInput>(CancellationToken);

        // technically not torrent name because it would lack extension on single file torrents but it's more convenient this way
        var torrentName = input.IsMultiFile ? input.CurrentPath.Split('/').Last() : Path.GetFileNameWithoutExtension(input.FilePath);

        var outp = new List<string>();

        var progressStopwatch = Stopwatch.StartNew();

        bool matchFolder(string path, int recursed = 0) {
            if (recursed >= input.RecurseLimit)
                return false;

            try {
                var currentDir = new DirectoryInfo(path);

                if (progressStopwatch.ElapsedMilliseconds >= 1000) {
                    Session.Progress.Text = path;
                    progressStopwatch.Restart();
                }

                foreach (var file in currentDir.EnumerateFiles()) {
                    if (file.FullName.EndsWith(input.FilePath)) {
                        outp.Add(file.FullName);
                        if (input.Size == file.Length) {
                            // Strong match, signal outside to bail
                            return true;
                        }
                        continue;
                    }
                    if (input.Size == file.Length) {
                        outp.Add(file.FullName);
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
        if (matchFolder(input.CurrentPath)) {
            goto done;
        }

        foreach (var path in input.AdditionalPaths) {
            try {
                var dir = new DirectoryInfo(path);
                foreach (var info in dir.EnumerateFileSystemInfos()) {
                    if (info.Attributes.HasFlag(FileAttributes.Directory)) {
                        if (info.Name == torrentName || input.WideSearch) {
                            Logger.LogTrace($"Considering {info.FullName}...");
                            if (matchFolder(info.FullName)) {
                                // Got a strong match, I don't think we will need other folders...
                                goto done;
                            }
                        }
                    } else {
                        if (info.FullName.EndsWith(input.FilePath) || input.WideSearch) {
                            try {
                                var thisFile = new FileInfo(info.FullName);
                                if (thisFile.Length == input.Size) {
                                    outp.Add(info.FullName);
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
        Session.Send(new SolveFileResult([.. outp]));
        Session.Progress.State = TASK_STATE.DONE;
    }
}
