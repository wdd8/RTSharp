using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using RTSharp.Daemon.Protocols;
using RTSharp.Daemon.RuntimeCompilation;
using RTSharp.Daemon.RuntimeCompilation.Exceptions;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

namespace RTSharp.Daemon.Services
{
    public class ServerService(ILogger<ServerService> Logger, SessionsService Sessions) : GRPCServerService.GRPCServerServiceBase
    {
        public override Task<Empty> Test(Empty request, ServerCallContext context) => Task.FromResult(new Empty());

        public override Task<ScriptSessionReply> StartScript(StartScriptInput Req, ServerCallContext context)
        {
            try {
                Logger.LogDebug("Compiling script...");
                Logger.LogTrace("Script: {script}", Req.Script);

                var script = Req.Script;

                var compilation = DynamicCompilation.Compile<IScript>(
                    ref script,
                    [ "RTSharp.Daemon", "RTSharp.Daemon.Protocols", "RTSharp.Shared.Abstractions", "RTSharp.Shared.Utils", "System.Collections", "System.Linq", "System.Text.Json", "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Logging" ],
                    [ "System", "System.Threading.Tasks", "System.Threading", "System.Collections.Generic", "RTSharp.Daemon", "RTSharp.Shared.Abstractions", "RTSharp.Shared.Utils" ]);
                
                Logger.LogDebug("Compilation successful, running script...");

                var session = Sessions.RunScript(compilation, Req.Variables.ToDictionary());

                Logger.LogInformation("Script ID {id} is running...", session.Id);

                session.Execution!.ContinueWith(task => {
                    Logger.LogInformation("Script ID {id} has finished", session.Id);
                }, TaskContinuationOptions.OnlyOnRanToCompletion);
                session.Execution!.ContinueWith(task => {
                    Logger.LogInformation($"Script ID {{id}} has errored: {task.Exception}", session.Id);
                }, TaskContinuationOptions.OnlyOnFaulted);
                session.Execution!.ContinueWith(task => {
                    Logger.LogInformation("Script ID {id} was cancelled", session.Id);
                }, TaskContinuationOptions.OnlyOnCanceled);

                return Task.FromResult(new ScriptSessionReply {
                    Id = session.Id.ToByteArray().ToByteString()
                });
            } catch (CompilationFailureException ex) {
                throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
            } catch (PragmaParsingException ex) {
                throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
            } catch (InstantiationException ex) {
                throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
            } catch (DllNotFoundException ex) {
                throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
            } catch (InterfaceNotFoundException ex) {
                throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
            }
        }

        Protocols.ScriptProgressState MapProgressState(ByteString Id, Shared.Abstractions.ScriptProgressState In)
        {
            return new Protocols.ScriptProgressState {
                Id = Id,
                Text = In.Text,
                Progress = In.Progress ?? -1,
                State = In.State switch {
                    TASK_STATE.WAITING => TaskState.Waiting,
                    TASK_STATE.RUNNING => TaskState.Running,
                    TASK_STATE.DONE => TaskState.Done,
                    _ => throw new IndexOutOfRangeException()
                },
                Chain = { In.Chain?.Select(x => MapProgressState(Id, x)) ?? [] }
            };
        }

        public override async Task ScriptsStatus(Empty Req, IServerStreamWriter<Protocols.ScriptProgressState> Res, ServerCallContext Ctx)
        {
            while (!Ctx.CancellationToken.IsCancellationRequested) {
                var sessions = Sessions.GetScriptSessions();

                await Task.WhenAll(sessions.Select(async x => {
                    await x.EvProgressChanged.WaitAsync(Ctx.CancellationToken);
                    await Res.WriteAsync(MapProgressState(x.Id.ToByteArray().ToByteString(), x.Progress), Ctx.CancellationToken);
                }));
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

                await session.EvProgressChanged.WaitAsync(Ctx.CancellationToken);
                Logger.LogInformation($"ScriptStatus: Session {id} progress changed, sending");
                await Res.WriteAsync(MapProgressState(Req.Value, session.Progress), Ctx.CancellationToken);
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
    }
}