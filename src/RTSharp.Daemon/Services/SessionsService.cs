using Nito.AsyncEx;

using RTSharp.Daemon.RuntimeCompilation;
using RTSharp.Daemon.RuntimeCompilation.Exceptions;
using RTSharp.Shared.Abstractions;

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

    internal AsyncAutoResetEvent EvProgressChanged = new(false);

    public void ProgressChanged()
    {
        var logger = Scope?.ServiceProvider.GetRequiredService<ILogger<SessionsService>>();
        logger?.LogInformation($"EV: Script session {Id} progress changed");
        EvProgressChanged.Set();
    }
}

public class SessionsService(IServiceScopeFactory ScopeFactory, ILogger<SessionsService> Logger)
{
    private readonly List<ScriptSession> Sessions = new();

    public ScriptSession RunScript(DynamicScript<IScript> DynamicScript, Dictionary<string, string> Variables)
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

        Sessions.Add(session = new ScriptSession {
            Id = id,
            Scope = scope,
            Script = DynamicScript,
            ScriptInstance = instance,
            Cts = cts,
            Progress = null!
        });

        var progress = new ScriptProgressState(session);
        session.Progress = progress;

        session.Execution = instance.Execute(Variables, session, cts.Token);
        BindContinuations(session);

        return session;
    }
    
    internal ScriptSession CreateSession(CancellationTokenSource Cts, Func<ScriptSession, Task> Fx)
    {
        ScriptSession session;
    
        Sessions.Add(session = new ScriptSession {
            Id = Guid.NewGuid(),
            Scope = null,
            Script = null,
            ScriptInstance = null,
            Cts = Cts,
            Progress = null!
        });
        
        var progress = new ScriptProgressState(session);
        session.Progress = progress;
        session.Execution = Fx(session);
        BindContinuations(session);
        
        return session;
    }

    private void BindContinuations(ScriptSession Session)
    {
        Session.Execution!.ContinueWith(x => {
            Logger.LogError(x.Exception, $"Session {Session.Id} failed");
            Session.Progress.State = TASK_STATE.FAILED;
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public IReadOnlyList<ScriptSession> GetScriptSessions()
    {
        return Sessions.AsReadOnly();
    }

    public ScriptSession? GetScriptSession(Guid Id)
    {
        return Sessions.FirstOrDefault(x => x.Id == Id);
    }

    public bool QueueScriptCancellation(Guid Id)
    {
        var session = Sessions.FirstOrDefault(x => x.Id == Id);

        if (session == null)
            return false;

        session.Cts.Cancel();

        return true;
    }
}
