using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.ClientFactory;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using RTSharp.Daemon.Protocols;
using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RTSharp.Shared.Abstractions.Daemon;
using ScriptProgressState = RTSharp.Shared.Abstractions.ScriptProgressState;
using Serilog;

namespace RTSharp.Core.Services.Daemon
{
    public class DaemonService : IDaemonService
    {
        GRPCServerService.GRPCServerServiceClient ServerClient;
        GRPCFilesService.GRPCFilesServiceClient FilesClient;
        IHostApplicationLifetime Lifetime;

        private Dictionary<string, Config.Models.Server> Servers;
        public string Id { get; }

        public string Host { get; }

        public ushort Port { get; }

        public Notifyable<TimeSpan> Latency { get; private set; } = new();

        public DaemonService(string ServerId, IHostApplicationLifetime Lifetime)
        {
            this.Id = ServerId;
            this.Lifetime = Lifetime;

            using var scope = Core.ServiceProvider.CreateScope();
            var clientFactory = scope.ServiceProvider.GetRequiredService<GrpcClientFactory>();
            var config = scope.ServiceProvider.GetRequiredService<Config>();

            ServerClient = clientFactory.CreateClient<GRPCServerService.GRPCServerServiceClient>(nameof(GRPCServerService.GRPCServerServiceClient) + "_" + ServerId);
            FilesClient = clientFactory.CreateClient<GRPCFilesService.GRPCFilesServiceClient>(nameof(GRPCFilesService.GRPCFilesServiceClient) + "_" + ServerId);
            Servers = config.Servers.Value;

            var cfg = Servers.First(x => x.Key == ServerId).Value;
            Host = cfg.Host;
            Port = cfg.DaemonPort;

            _ = Task.Run(PingLoop);
        }

        public async Task PingLoop()
        {
            while (!Lifetime.ApplicationStopping.IsCancellationRequested) {
                var sw = Stopwatch.StartNew();
                try {
                    await Ping(Lifetime.ApplicationStopping);
                    sw.Stop();
                    Latency.Change(sw.Elapsed);
                } catch {
                    sw.Stop();
                    Log.Logger.Error($"Ping failed for {Host}:{Port}");
                }

                if (sw.Elapsed < TimeSpan.FromSeconds(1))
                    await Task.Delay(TimeSpan.FromSeconds(1) - sw.Elapsed);
            }
        }

        public async Task Ping(CancellationToken cancellationToken)
        {
            await ServerClient.TestAsync(new Empty(), cancellationToken: cancellationToken);
        }

        record Path(string StorePath, string RemoteSourcePath, ulong TotalSize);

        public async Task RequestReceiveFiles(IEnumerable<(string RemoteSource, string StoreTo, ulong TotalSize)> Paths, string SenderServerId, IProgress<(string File, float Progress)> Progress)
        {
            var senderServer = Servers[SenderServerId].GetUri();

            var script = await ServerClient.StartScriptAsync(new StartScriptInput {
                Script =
"""
#pragma usings
#pragma lib "Microsoft.Extensions.Logging.Abstractions"

using RTSharp.Daemon.Services;
using System.Text.Json;
using System.Linq;
using Microsoft.Extensions.Logging;

public class Main(ChannelsService Channels, FileTransferService FileTransfer, ILogger<Main> Logger) : IScript
{
    record Path(string StorePath, string RemoteSourcePath, ulong TotalSize);

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
        var paths = JsonSerializer.Deserialize<List<Path>>(pathsStr)!;

        var channel = await Channels.GetFilesClient(target);

        transfer.State = TASK_STATE.RUNNING;

        await FileTransfer.ReceiveFilesFromRemote(channel, paths.Select(x => (x.StorePath, x.RemoteSourcePath)), new Progress<FileTransferService.FileTransferSessionProgress>(progress => {
            transfer.Text = progress.Path;

            if (progress.Path == "") {
                transfer.Text = "";
                transfer.Progress = 100f;
                transfer.State = TASK_STATE.DONE;
                return;
            }

            Logger.LogInformation($"Looking up path {progress.Path}...");

            var path = paths.First(x => progress.Path.EndsWith(x.StorePath));

            transfer.Progress = progress.BytesReceived / (float)path.TotalSize * 100f;
        }));

        Session.Progress.State = TASK_STATE.DONE;
    }
}
""",
                Name = "RequstReceiveFiles",
                Variables = {
                    { "Uri", senderServer.ToString() },
                    { "Paths", JsonSerializer.Serialize(Paths.Select(x => new Path(x.StoreTo, x.RemoteSource, x.TotalSize))) }
                }
            });

            var id = script.Id;

            var progress = ServerClient.ScriptStatus(new BytesValue() { Value = id });

            await foreach (var info in progress.ResponseStream.ReadAllAsync()) {
                if (info.State == TaskState.Done)
                    return;

                Progress.Report((info.Text, info.Progress));
            }
        }

        public async Task<MemoryStream> ReceiveFilesInline(string RemotePath)
        {
            var session = FilesClient.Internal_SendFiles(new SendFilesInput {
                Paths = { RemotePath }
            });

            var mem = new MemoryStream();

            await foreach (var block in session.ResponseStream.ReadAllAsync()) {
                if (block.DataCase == FileBuffer.DataOneofCase.Buffer) {
                    mem.Write(block.Buffer.Span);
                }
            }

            return mem;
        }

        public async Task<Shared.Abstractions.FileSystemItem> GetDirectoryInfo(string Path)
        {
            RTSharp.Daemon.Protocols.FileSystemItem dir;
            try {
                dir = await FilesClient.GetDirectoryInfoAsync(new StringValue { Value = Path });
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Failed to GetDirectoryInfo");
                throw;
            }

            static Shared.Abstractions.FileSystemItem map(RTSharp.Daemon.Protocols.FileSystemItem In)
            {
                return new Shared.Abstractions.FileSystemItem(
                    Path: In.Path,
                    Directory: In.Directory,
                    LastModified: In.LastModified.ToDateTime(),
                    Permissions: In.Permissions,
                    Size: In.Size,
                    Children: In.Children.Select(map).ToList()
                );
            }

            return map(dir);
        }

        public async Task CreateDirectory(string Path)
        {
            try {
                await FilesClient.CreateDirectoryAsync(new StringValue { Value = Path });
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Failed to CreateDirectory");
                throw;
            }
        }

        public async Task<Dictionary<string, bool>> CheckExists(IList<string> Files)
        {
            try {
                return (await FilesClient.CheckExistsAsync(new CheckExistsInput {
                    Paths = { Files }
                })).Existence.ToDictionary(x => x.Key, x => x.Value);
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Failed to CheckExists");
                throw;
            }
        }

        public async Task RemoveEmptyDirectory(string Path)
        {
            try {
                await FilesClient.RemoveEmptyDirectoryAsync(new StringValue { Value = Path });
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Failed to RemoveEmptyDirectory");
                throw;
            }
        }

        public async Task<IList<string>> Mediainfo(IList<string> Files)
        {
            MediaInfoReply reply;
            try {
                reply = await FilesClient.MediaInfoAsync(new MediaInfoInput() {
                    Paths = { Files }
                });
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Failed to Mediainfo");
                throw;
            }

            return reply.Output;
        }

        public async Task<Guid> RunCustomScript(string Script, string Name, Dictionary<string, string> Variables)
        {
            var session = await ServerClient.StartScriptAsync(new StartScriptInput {
                Script = Script,
                Name = Name,
                Variables = { Variables }
            });

            return new Guid(session.Id.Span);
        }

        public async Task QueueScriptCancellation(Guid Id)
        {
            await ServerClient.StopScriptAsync(new BytesValue { Value = ByteString.CopyFrom(Id.ToByteArray()) });
        }

        public async Task GetScriptProgress(Guid Id, IProgress<ScriptProgressState>? Progress)
        {
            var cts = new CancellationTokenSource();
            var stream = ServerClient.ScriptStatus(Id.ToByteArray().ToBytesValue(), cancellationToken: cts.Token);
            
            ScriptProgressState mapProgressState(RTSharp.Daemon.Protocols.ScriptProgressState In)
            {
                return new ScriptProgressState
                {
                    Progress = In.Progress,
                    State = In.State switch {
                        TaskState.Done => TASK_STATE.DONE,
                        TaskState.Running => TASK_STATE.RUNNING,
                        TaskState.Waiting => TASK_STATE.WAITING,
                        TaskState.Failed => TASK_STATE.FAILED,
                        _ => throw new ArgumentOutOfRangeException()
                    },
                    Text = In.Text,
                    StateData = In.StateData,
                    Chain = In.Chain?.Select(mapProgressState).ToArray() ?? []
                };
            }
            
            try {
                await foreach (var progressData in stream.ResponseStream.ReadAllAsync()) {
                    Progress?.Report(mapProgressState(progressData));
                    if (progressData.State == TaskState.Done || progressData.State == TaskState.Failed) {
                        cts.Cancel();
                    }
                }
            } catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }
        }
        
        public async Task<Dictionary<string, bool>> AllowedToDeleteFiles(IEnumerable<string> In)
        {
            var reply = await FilesClient.AllowedToDeleteAsync(new AllowedToDeleteInput
            {
                Paths = { In }
            });
            
            return reply.Reply.ToDictionary(x => x.Path, x => x.Value);
        }
        
        public async Task<Dictionary<string, bool>> AllowedToReadFiles(IEnumerable<string> In)
        {
            var reply = await FilesClient.AllowedToReadAsync(new AllowedToReadInput
            {
                Paths = { In }
            });
            
            return reply.Reply.ToDictionary(x => x.Path, x => x.Value);
        }


        public T GetGrpcService<T>()
            where T : ClientBase<T>
        {
            using var scope = Core.ServiceProvider.CreateScope();
            var clientFactory = scope.ServiceProvider.GetRequiredService<GrpcClientFactory>();

            return clientFactory.CreateClient<T>(typeof(T).Name + "_" + Id);
        }

        public IDaemonTorrentsService GetTorrentsService(IDataProvider DataProvider) {
            return new DaemonTorrentsService(Plugins.DataProviders.First(x => x.Instance == DataProvider));
        }
    }
}
