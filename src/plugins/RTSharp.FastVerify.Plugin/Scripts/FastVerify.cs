#pragma usings

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

    public async Task Execute(Dictionary<string, string> Variables, IScriptSession Session, CancellationToken CancellationToken)
    {
        Session.Progress.State = TASK_STATE.RUNNING;
        Session.Progress.Text = "Fast verifying...";

        try {
            var bytes = CompactBinaryString.Decode(Variables["TorrentEncoded"]);
            var baseDirectory = Variables["BaseDirectory"];

            using var mem = new MemoryStream(bytes);
            var bTorrent = new BencodeParser().Parse<BDictionary>(mem);

            var logger = new CapturingLogger();
            var verified = await BencodeUtils.FastVerifyFilesOnDisk(bTorrent, baseDirectory, logger);

            Session.Progress.StateData = $"{(verified ? "1" : "0")}\x00{logger.LastWarning}";
            Session.Progress.State = TASK_STATE.DONE;
        } catch (Exception ex) {
            Logger.LogError(ex, "Fast verify script failed");
            Session.Progress.StateData = $"0\x00{ex.Message}";
            Session.Progress.State = TASK_STATE.FAILED;
        }
    }
}
