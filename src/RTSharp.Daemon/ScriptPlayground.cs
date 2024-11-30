using RTSharp.Daemon.Services;
using RTSharp.Shared.Abstractions;

using System.Text.Json;

namespace RTSharp.Daemon
{
    public class ScriptPlayground(ChannelsService Channels, FileTransferService FileTransfer) : IScript
    {
        record Paths(string StorePath, string RemoteSourcePath, ulong TotalSize);

        public async Task Execute(Dictionary<string, string> Variables, IScriptSession Session, CancellationToken CancellationToken)
        {
            Session.Progress.State = TASK_STATE.RUNNING;
            var transfer = new ScriptProgressState(Session) {
                Text = "Transfering files...",
                Progress = 0f,
                State = TASK_STATE.WAITING
            };
            Session.Progress.Chain = [ transfer ];

            var target = Variables["Uri"];
            var pathsStr = Variables["Paths"];
            var paths = JsonSerializer.Deserialize<List<Paths>>(pathsStr)!;

            var channel = await Channels.GetFilesClient(target);

            transfer.State = TASK_STATE.RUNNING;

            await FileTransfer.ReceiveFilesFromRemote(channel, paths.Select(x => (x.StorePath, x.RemoteSourcePath)), new Progress<FileTransferService.FileTransferSessionProgress>(progress => {
                transfer.Text = progress.Path;

                if (progress.Path == "") {
                    transfer.Text = "";
                    transfer.Progress = 100f;
                    transfer.State = TASK_STATE.DONE;
                }

                var path = paths.First(x => progress.Path.EndsWith(x.RemoteSourcePath));

                transfer.Progress = progress.BytesReceived / (float)path.TotalSize * 100f;
            }));

            Session.Progress.State = TASK_STATE.DONE;
        }
    }
}
