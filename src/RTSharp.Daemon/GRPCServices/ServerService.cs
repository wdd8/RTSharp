using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using RTSharp.Daemon.Protocols;
using RTSharp.Daemon.RuntimeCompilation;
using RTSharp.Daemon.RuntimeCompilation.Exceptions;
using RTSharp.Daemon.Services;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

using System.Collections.Concurrent;

namespace RTSharp.Daemon.GRPCServices
{
    public class ServerService(ILogger<ServerService> Logger, SessionsService Sessions) : GRPCServerService.GRPCServerServiceBase
    {
        public override Task<Empty> Test(Empty request, ServerCallContext context) => Task.FromResult(new Empty());

        private static ConcurrentDictionary<byte[], DynamicScript<IScript>> Cache = new ConcurrentDictionary<byte[], DynamicScript<IScript>>(new ByteArrayComparer());

        public override Task<ScriptSessionReply> StartScript(StartScriptInput Req, ServerCallContext context)
        {
            try {
                Logger.LogDebug("Compiling script...");
                Logger.LogTrace("Script: {script}", Req.Script);

                var script = Req.Script;
                var hash = System.Security.Cryptography.SHA512.HashData(System.Text.Encoding.UTF8.GetBytes(script));
                if (!Cache.TryGetValue(hash, out var compilation)) {
                    compilation = DynamicCompilation.Compile<IScript>(
                        ref script,
                        Req.Name,
                        [ "RTSharp.Daemon", "RTSharp.Daemon.Protocols", "RTSharp.Shared.Abstractions", "RTSharp.Shared.Utils", "System.Collections", "System.Linq", "System.Text.Json", "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Logging", "System.ComponentModel" ],
                        [ "System", "System.Threading.Tasks", "System.Threading", "System.Collections.Generic", "RTSharp.Daemon", "RTSharp.Shared.Abstractions", "RTSharp.Shared.Utils" ]);
                    Logger.LogDebug("Compilation successful, running script...");
                    Cache[hash] = compilation;
                } else {
                    Logger.LogDebug($"Cached script: {Convert.ToBase64String(hash)}");
                }

                var session = Sessions.RunScript(Req.Name, compilation, Req.Variables.ToDictionary());

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
                    Id = session.Id.ToByteString()
                });
            } catch (CompilationFailureException ex) {
                throw new RpcException(new Status(StatusCode.InvalidArgument, string.Join('\n', ex.Message.Split([ "\r\n", "\n" ], StringSplitOptions.None).Where(x => !x.StartsWith("warning")))));
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

        Protocols.ScriptProgressState MapProgressState(Shared.Abstractions.ScriptProgressState In, bool includeChain)
        {
            return new Protocols.ScriptProgressState {
                Id = In.Id.ToByteString(),
                Text = In.Text,
                Progress = In.Progress ?? -1,
                State = In.State switch {
                    TASK_STATE.WAITING => TaskState.Waiting,
                    TASK_STATE.RUNNING => TaskState.Running,
                    TASK_STATE.FAILED => TaskState.Failed,
                    TASK_STATE.DONE => TaskState.Done,
                    _ => throw new IndexOutOfRangeException()
                },
                Chain = { includeChain ? In.Chain?.Select(x => MapProgressState(x, includeChain: true)) ?? [] : [] },
                StateData = In.StateData
            };
        }

        Protocols.ScriptSessionState MapScriptSession(ScriptSession In)
        {
            return new Protocols.ScriptSessionState {
                Id = In.Id.ToByteString(),
                Name = In.Name,
                Progress = MapProgressState(In.Progress, includeChain: true)
            };
        }

        Protocols.ScriptSessionsUpdate MapScriptSessionUpdate(ScriptSessionStatusUpdate In)
        {
            var ret = new Protocols.ScriptSessionsUpdate();

            if (In.FullUpdate) {
                ret.FullUpdate = new Protocols.ScriptSessionsFullUpdate {
                    Sessions = { In.Sessions.Select(MapScriptSession) }
                };
            } else {
                ret.DeltaUpdate = new Protocols.ScriptSessionDeltaUpdate {
                    SessionId = In.SessionId.ToByteString(),
                    State = In.Progress != null ? MapProgressState(In.Progress, In.IncludeChain) : null
                };

                if (In.ParentStateId != null) {
                    ret.DeltaUpdate.ParentStateId = In.ParentStateId.Value.ToByteString();
                }
            }

            return ret;
        }

        public override async Task ScriptsStatus(Empty Req, IServerStreamWriter<Protocols.ScriptSessionsUpdate> Res, ServerCallContext Ctx)
        {
            var ct = Ctx.CancellationToken;
            await foreach (var update in Sessions.MonitorSessionUpdates(ct).ReadAllAsync(ct)) {
                await Res.WriteAsync(MapScriptSessionUpdate(update), ct);
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

                Logger.LogInformation($"ScriptStatus: Session {id} progress changed, sending");
                await Res.WriteAsync(MapProgressState(session.Progress, includeChain: true), Ctx.CancellationToken);

                if (task == session.Execution) {
                    return;
                }
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
    }
}
