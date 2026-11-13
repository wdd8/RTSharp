#pragma usings
#pragma contract FastVerifyMessages

using System.IO;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using BencodeNET.Torrents;
using Microsoft.Extensions.Logging;

namespace RTSharp.Scripts.FastVerify;

public class Main(ILogger<Main> Logger) : IScript
{
    private sealed class CapturingLogger : ILogger
    {
        public string? LastWarning;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                LastWarning = formatter(state, exception);
        }
    }

    public async Task Execute(IScriptSession Session, CancellationToken CancellationToken)
    {
        Session.Progress.State = TASK_STATE.RUNNING;
        Session.Progress.Text = "Fast verifying...";

        try {
            var input = await Session.Rx<FastVerifyInput>(CancellationToken);

            using var mem = new MemoryStream(input.Torrent);
            var bTorrent = new BencodeParser().Parse<BDictionary>(mem);

            var logger = new CapturingLogger();
            var verified = await BencodeUtils.FastVerifyFilesOnDisk(bTorrent, x => BencodeUtils.GetFilesOnDisk(x, input.BaseDirectory), logger, (file, done, total) => {
                Session.Progress.Text = $"Fast verifying {Path.GetFileName(file.Path)}: {done}/{total} pieces";
                Session.Progress.Progress = done * 100f / total;
            });

            Session.Send(new FastVerifyResult(verified, logger.LastWarning));
            Session.Progress.State = TASK_STATE.DONE;
        } catch (Exception ex) {
            Logger.LogError(ex, "Fast verify script failed");
            Session.Send(new FastVerifyResult(false, ex.Message));
            Session.Progress.State = TASK_STATE.FAILED;
        }
    }
}
