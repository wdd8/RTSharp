#pragma usings
#pragma contract RequestReceiveFilesMessages

using RTSharp.Daemon.Services;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace RTSharp.Scripts.RequestReceiveFiles;

public class Main(ChannelsService Channels, FileTransferService FileTransfer, ILogger<Main> Logger) : IScript
{
    public async Task Execute(IScriptSession Session, CancellationToken CancellationToken)
    {
        Session.Progress.State = TASK_STATE.RUNNING;
        var transfer = new ScriptProgressState(Session) {
            Text = "Transfering files...",
            Progress = 0f,
            State = TASK_STATE.WAITING
        };
        Session.Progress.Chain = [ transfer ];

        var input = await Session.Rx<RequestReceiveFilesInput>(CancellationToken);

        var channel = Channels.GetFilesClient(input.Uri);

        transfer.State = TASK_STATE.RUNNING;

        await FileTransfer.ReceiveFilesFromRemote(channel, input.Paths.Select(x => (x.StorePath, x.RemoteSourcePath)), (FileTransferService.FileTransferSessionProgress progress) => {
            transfer.Text = progress.Path;

            if (progress.Path == "") {
                transfer.Text = "";
                transfer.Progress = 100f;
                transfer.State = TASK_STATE.DONE;
                return;
            }

            Logger.LogInformation($"Looking up path {progress.Path}...");

            var path = input.Paths.First(x => progress.Path.EndsWith(x.StorePath));

            transfer.Progress = progress.BytesReceived / (float)path.TotalSize * 100f;
        });

        Session.Progress.State = TASK_STATE.DONE;
    }
}
