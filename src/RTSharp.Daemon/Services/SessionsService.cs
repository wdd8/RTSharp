using Nito.AsyncEx;

using RTSharp.Daemon.RuntimeCompilation;
using RTSharp.Daemon.RuntimeCompilation.Exceptions;
using RTSharp.Shared.Abstractions;

using System.Threading.Channels;

namespace RTSharp.Daemon.Services;

public class ScriptSession : IScriptSession
{
    public required Guid Id { get; init; }

    public required IServiceScope Scope { get; init; }

    public required DynamicScript<IScript> Script { get; init; }

    public required IScript ScriptInstance { get; init; }

    public Task? Execution { get; set; }

    public required CancellationTokenSource Cts { get; init; }

    public ScriptProgressState Progress { get; internal set; }

    public required string Name { get; set; }

    internal AsyncAutoResetEvent EvProgressChanged = new(false);

    internal event Action<ScriptSession, ScriptProgressState, bool>? ProgressUpdated;

    public void ProgressChanged(ScriptProgressState? progress = null, bool chainChanged = false)
    {
        var logger = Scope?.ServiceProvider.GetRequiredService<ILogger<SessionsService>>();
        logger?.LogInformation($"EV: Script session {Id} progress changed");

        EvProgressChanged.Set();
        ProgressUpdated?.Invoke(this, progress ?? Progress, chainChanged);
    }
}

public class ScriptSessionStatusUpdate
{
    public required bool FullUpdate { get; init; }

    public IReadOnlyList<ScriptSession> Sessions { get; init; } = [];

    public Guid SessionId { get; init; }

    public Guid? ParentStateId { get; init; }

    public ScriptProgressState? Progress { get; init; }

    public bool IncludeChain { get; init; }
}

public class SessionsService(IServiceScopeFactory ScopeFactory, ILogger<SessionsService> Logger)
{
    private readonly List<ScriptSession> Sessions = new();

    private readonly object SessionsLock = new();

    private readonly List<ChannelWriter<ScriptSessionStatusUpdate>> Subscribers = new();

    public ScriptSession RunScript(string Name, DynamicScript<IScript> DynamicScript, Dictionary<string, string> Variables)
    {
        var scope = ScopeFactory.CreateScope();
        var cts = new CancellationTokenSource();
        var id = Guid.NewGuid();
        ScriptSession session;
        IScript? instance;

        try {
            instance = (IScript?)ActivatorUtilities.CreateInstance(scope.ServiceProvider, DynamicScript.ClassType);
        } catch (Exception ex) {
            throw new InstantiationException(ex.Message);
        }

        if (instance == null) {
            throw new InstantiationException($"{DynamicScript.ClassType.Name} has not been resolved");
        }

        session = new ScriptSession {
            Id = id,
            Name = Name,
            Scope = scope,
            Script = DynamicScript,
            ScriptInstance = instance,
            Cts = cts,
            Progress = null!
        };

        var progress = new ScriptProgressState(id, session);
        session.Progress = progress;
        RegisterSession(session);

        session.Execution = instance.Execute(Variables, session, cts.Token);
        BindContinuations(session);

        return session;
    }

    internal ScriptSession CreateSession(string Name, CancellationTokenSource Cts, Func<ScriptSession, Task> Fx)
    {
        ScriptSession session;

        session = new ScriptSession {
            Id = Guid.NewGuid(),
            Name = Name,
            Scope = null,
            Script = null,
            ScriptInstance = null,
            Cts = Cts,
            Progress = null!
        };

        var progress = new ScriptProgressState(session.Id, session);
        session.Progress = progress;
        RegisterSession(session);
        session.Execution = Fx(session);
        BindContinuations(session);
        
        return session;
    }

    private void RegisterSession(ScriptSession session)
    {
        session.ProgressUpdated += PublishProgressChanged;
        lock (SessionsLock) {
            Sessions.Add(session);
            Publish(session.Id, null, session.Progress, true);
        }
    }

    private void BindContinuations(ScriptSession Session)
    {
        Session.Execution!.ContinueWith(x => {
            Logger.LogError(x.Exception, $"Session {Session.Id} failed");
            Session.Progress.State = TASK_STATE.FAILED;
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public ChannelReader<ScriptSessionStatusUpdate> MonitorSessionUpdates(CancellationToken CancellationToken)
    {
        var channel = Channel.CreateUnbounded<ScriptSessionStatusUpdate>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false
        });

        lock (SessionsLock) {
            channel.Writer.TryWrite(new ScriptSessionStatusUpdate {
                FullUpdate = true,
                Sessions = [.. Sessions]
            });
            Subscribers.Add(channel.Writer);
        }

        CancellationToken.Register(() => {
            lock (SessionsLock) {
                Subscribers.Remove(channel.Writer);
                channel.Writer.TryComplete();
            }
        });

        return channel.Reader;
    }

    private void PublishProgressChanged(ScriptSession session, ScriptProgressState progress, bool chainChanged)
    {
        lock (SessionsLock) {
            Guid? parentStateId = null;
            if (progress.Id != session.Progress.Id) {
                parentStateId = FindParentStateId(session.Progress, progress.Id); // TODO: optimization either have dict or store parent id in state
                if (parentStateId == null) {
                    return;
                }
            }

            Publish(session.Id, parentStateId, progress, chainChanged);
        }
    }

    private void Publish(Guid SessionId, Guid? ParentStateId, ScriptProgressState Progress, bool IncludeChain)
    {
        for (var x = Subscribers.Count - 1; x >= 0; x--) {
            var ret = Subscribers[x].TryWrite(new ScriptSessionStatusUpdate {
                FullUpdate = false,
                SessionId = SessionId,
                ParentStateId = ParentStateId,
                Progress = Progress,
                IncludeChain = IncludeChain
            });

            if (!ret) {
                Subscribers.RemoveAt(x);
            }
        }
    }

    private static Guid? FindParentStateId(ScriptProgressState root, Guid stateId)
    {
        if (root.Chain == null) {
            return null;
        }

        foreach (var child in root.Chain) {
            if (child.Id == stateId) {
                return root.Id;
            }

            var found = FindParentStateId(child, stateId);
            if (found != null) {
                return found;
            }
        }

        return null;
    }

    public ScriptSession? GetScriptSession(Guid Id)
    {
        lock (SessionsLock) {
            return Sessions.FirstOrDefault(x => x.Id == Id);
        }
    }

    public bool QueueScriptCancellation(Guid Id)
    {
        ScriptSession? session;
        lock (SessionsLock) {
            session = Sessions.FirstOrDefault(x => x.Id == Id);
        }

        if (session == null)
            return false;

        session.Cts.Cancel();

        return true;
    }

    public bool RemoveCompletedScriptSession(Guid Id)
    {
        ScriptSession? session;
        lock (SessionsLock) {
            session = Sessions.FirstOrDefault(x => x.Id == Id);
            if (session == null || session.Progress.State != TASK_STATE.DONE) {
                return false;
            }

            session.ProgressUpdated -= PublishProgressChanged;
            Sessions.Remove(session);
        }

        Publish(session.Id, null, session.Progress, true);

        session.Cts.Dispose();
        session.Scope?.Dispose();

        return true;
    }
}
