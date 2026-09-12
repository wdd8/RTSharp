using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Microsoft.Extensions.Options;

using RTSharp.Daemon.Protocols;
using RTSharp.Daemon.RuntimeCompilation;
using RTSharp.Daemon.Services;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

namespace RTSharp.Daemon.GRPCServices
{
    public class ServerService(ILogger<ServerService> Logger, SessionsService Sessions, InstalledScriptsService InstalledScripts, IOptionsMonitor<ServerOptions> Options) : GRPCServerService.GRPCServerServiceBase
    {
        public override Task<Empty> Test(Empty request, ServerCallContext context) => Task.FromResult(new Empty());

        public override Task<ServerSettings> GetServerSettings(Empty request, ServerCallContext context)
        {
            var options = Options.CurrentValue;
            var ret = new ServerSettings();

            if (options.DLBandwidth != null)
                ret.DLBandwidth = options.DLBandwidth.Value;
            if (options.UPBandwidth != null)
                ret.UPBandwidth = options.UPBandwidth.Value;

            return Task.FromResult(ret);
        }

        public override async Task<ScriptSessionReply> StartScript(StartScriptInput Req, ServerCallContext context)
        {
            try {
                Logger.LogDebug("Compiling script...");
                Logger.LogTrace("Script: {script}", Req.Script);

                var compilation = await DynamicCompilation.Compile<IScript>(Req.Script, Req.Name);

                var session = Sessions.RunScript(Req.Name, compilation, Req.Input.Select(x => x.ToScriptMessage()));

                Logger.LogInformation("Script ID {id} is running...", session.Id);

                _ = session.Execution!.ContinueWith(task => {
                    Logger.LogInformation("Script ID {id} has finished", session.Id);
                }, TaskContinuationOptions.OnlyOnRanToCompletion);
                _ = session.Execution!.ContinueWith(task => {
                    Logger.LogError(task.Exception, "Script ID {id} has errored", session.Id);
                }, TaskContinuationOptions.OnlyOnFaulted);
                _ = session.Execution!.ContinueWith(task => {
                    Logger.LogInformation("Script ID {id} was cancelled", session.Id);
                }, TaskContinuationOptions.OnlyOnCanceled);

                return new ScriptSessionReply {
                    Id = session.Id.ToByteString()
                };
            } catch (Exception ex) when (DynamicCompilation.TryDescribeAuthoringError(ex, out var message)) {
                throw new RpcException(new Status(StatusCode.InvalidArgument, message));
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to compile script");
                throw;
            }
        }

        static TaskState MapTaskState(TASK_STATE In) => In switch {
            TASK_STATE.WAITING => TaskState.Waiting,
            TASK_STATE.RUNNING => TaskState.Running,
            TASK_STATE.FAILED => TaskState.Failed,
            TASK_STATE.DONE => TaskState.Done,
            TASK_STATE.CANCELLED => TaskState.Cancelled,
            _ => throw new IndexOutOfRangeException()
        };

        Protocols.ScriptProgressState MapProgressState(Shared.Abstractions.ScriptProgressState In, bool includeChain)
        {
            return new Protocols.ScriptProgressState {
                Id = In.Id.ToByteString(),
                Text = In.Text,
                Progress = In.Progress ?? -1,
                State = MapTaskState(In.State),
                Chain = { includeChain ? In.Chain?.Select(x => MapProgressState(x, includeChain: true)) ?? [] : [] }
            };
        }

        Protocols.ScriptSessionUpdate MapScriptSessionUpdate(Shared.Abstractions.ScriptSessionUpdate In, bool IncludeChain)
        {
            var ret = new Protocols.ScriptSessionUpdate {
                SessionId = In.SessionId.ToByteString(),
                SessionName = In.SessionName ?? "",
                State = MapProgressState(In.State, IncludeChain),
                InstalledScriptId = In.InstalledScriptId
            };

            if (In.ParentStateId != null) {
                ret.ParentStateId = In.ParentStateId.Value.ToByteString();
            }

            return ret;
        }

        public override async Task ScriptsStatus(ScriptSessionsStatusRequest Req, IServerStreamWriter<Protocols.ScriptSessionUpdate> Res, ServerCallContext Ctx)
        {
            var ct = Ctx.CancellationToken;
            var includeFullUpdate = Req.Mode.HasFlag(ScriptSessionsStatusMode.FullUpdate);
            var streamUpdates = Req.Mode.HasFlag(ScriptSessionsStatusMode.Streaming);

            await foreach (var (update, includeChain) in Sessions.MonitorSessionUpdates(includeFullUpdate, streamUpdates, ct).ReadAllAsync(ct)) {
                await Res.WriteAsync(MapScriptSessionUpdate(update, includeChain), ct);
            }
        }

        public override async Task ScriptStatus(BytesValue Req, IServerStreamWriter<Protocols.ScriptProgressState> Res, ServerCallContext Ctx)
        {
            if (Req.Value.Length != 16)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Argument must be exactly 16 bytes long"));
            
            var id = new Guid(Req.Value.Span);

            while (!Ctx.CancellationToken.IsCancellationRequested) {
                var session = Sessions.GetScriptSession(id);

                if (session == null) {
                    Logger.LogInformation($"ScriptStatus: Session {id} doesn't exist");
                    break;
                }

                var task = await Task.WhenAny(session.Execution!, session.EvProgressChanged.WaitAsync(Ctx.CancellationToken));

                Logger.LogTrace($"ScriptStatus: Session {id} progress changed, sending");
                await Res.WriteAsync(MapProgressState(session.Progress, includeChain: true), Ctx.CancellationToken);

                if (task == session.Execution) {
                    return;
                }
            }
        }

        public override async Task ScriptChannel(IAsyncStreamReader<ScriptChannelRequest> Req, IServerStreamWriter<Protocols.ScriptMessage> Res, ServerCallContext Ctx)
        {
            if (!await Req.MoveNext(Ctx.CancellationToken))
                return;

            if (!Req.Current.HasAttach || Req.Current.Attach.Length != 16)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Missing session attach"));

            var id = new Guid(Req.Current.Attach.Span);
            var session = Sessions.GetScriptSession(id) ?? throw new RpcException(new Status(StatusCode.NotFound, "Script session not found"));

            Logger.LogTrace($"ScriptChannel: attached to session {id}");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ctx.CancellationToken);

            var input = Task.Run(async () => {
                try {
                    while (await Req.MoveNext(cts.Token)) {
                        session.ReceivedMessage(Req.Current.Input.ToScriptMessage());
                    }
                } catch (Exception ex) when (ex is OperationCanceledException or RpcException) {
                }
            });

            try {
                await foreach (var message in session.SubscribeOutput(Ctx.CancellationToken).ReadAllAsync(Ctx.CancellationToken))
                    await Res.WriteAsync(message.ToProtocol(), Ctx.CancellationToken);
            } finally {
                await cts.CancelAsync();
                await input;
            }
        }

        public override Task<Empty> StopScript(BytesValue Req, ServerCallContext Ctx)
        {
            if (Req.Value.Length != 16)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Argument must be exactly 16 bytes long"));

            var id = new Guid(Req.Value.Span);

            if (!Sessions.QueueScriptCancellation(id)) {
                 throw new RpcException(new Status(StatusCode.NotFound, "Script session not found"));
            }

            return Task.FromResult(new Empty());
        }

        public override Task<Empty> RemoveScript(BytesValue Req, ServerCallContext Ctx)
        {
            var id = new Guid(Req.Value.Span);

            if (!Sessions.RemoveCompletedScriptSession(id)) {
                throw new RpcException(new Status(StatusCode.NotFound, "Completed script session not found"));
            }

            return Task.FromResult(new Empty());
        }

        Protocols.InstalledScript MapInstalledScript(Services.InstalledScript In)
        {
            var failure = In.Session?.Execution?.Exception?.GetBaseException();

            var lastError = "";
            if (failure != null) {
                lastError = DynamicCompilation.TryDescribeAuthoringError(failure, out var message) ? message : failure.Message;
            }

            var ret = new Protocols.InstalledScript {
                Id = In.Id,
                Name = In.Start.Name,
                OwnerId = In.Info.OwnerId,
                ScriptHash = ByteString.CopyFrom(In.ScriptHash),
                OrphanAcknowledged = In.Info.OrphanAcknowledged,
                State = MapTaskState(In.Session?.Progress.State ?? TASK_STATE.WAITING),
                StatusText = In.Session?.Progress.Text ?? "",
                LastError = lastError,
                InstalledAt = Timestamp.FromDateTimeOffset(In.Info.InstalledAt)
            };

            if (In.Session != null) {
                ret.SessionId = In.Session.Id.ToByteString();
                ret.StartedAt = Timestamp.FromDateTimeOffset(In.Session.StartedAt);
            }

            return ret;
        }

        public override async Task<Protocols.InstalledScript> InstallScript(InstallScriptInput Req, ServerCallContext Ctx)
        {
            try {
                var installed = await InstalledScripts.Install(Req.Id, Req.OwnerId, Req.Start);

                return MapInstalledScript(installed);
            } catch (ArgumentException ex) {
                throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
            } catch (Exception ex) when (DynamicCompilation.TryDescribeAuthoringError(ex, out var message)) {
                throw new RpcException(new Status(StatusCode.InvalidArgument, message));
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to install script {id}", Req.Id);
                throw;
            }
        }

        public override async Task<Empty> UninstallScript(StringValue Req, ServerCallContext Ctx)
        {
            try {
                await InstalledScripts.Uninstall(Req.Value);
            } catch (KeyNotFoundException ex) {
                throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
            }

            return new Empty();
        }

        public override async Task<InstalledScriptList> ListInstalledScripts(Empty Req, ServerCallContext Ctx)
        {
            var ret = new InstalledScriptList();

            foreach (var script in await InstalledScripts.List(Ctx.CancellationToken))
                ret.Scripts.Add(MapInstalledScript(script));

            return ret;
        }

        public override async Task<Protocols.InstalledScript> AcknowledgeOrphanedScript(StringValue Req, ServerCallContext Ctx)
        {
            try {
                return MapInstalledScript(await InstalledScripts.AcknowledgeOrphaned(Req.Value));
            } catch (Exception ex) when (ex is KeyNotFoundException or DirectoryNotFoundException or FileNotFoundException) {
                throw new RpcException(new Status(StatusCode.NotFound, $"No installed script '{Req.Value}'"));
            }
        }
    }
}
