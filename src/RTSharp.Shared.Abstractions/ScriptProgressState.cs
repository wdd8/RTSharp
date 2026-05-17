#nullable enable

namespace RTSharp.Shared.Abstractions;

public enum TASK_STATE
{
    WAITING,
    RUNNING,
    DONE,
    FAILED
}

public class ScriptProgressState
{
    private readonly IScriptSession? Base;

    public ScriptProgressState(IScriptSession Base)
    {
        this.Base = Base;
        Id = Guid.NewGuid();
    }

    public ScriptProgressState(Guid Id, IScriptSession Base)
    {
        this.Base = Base;
        this.Id = Id;
    }
    
    public ScriptProgressState()
    {
        Id = Guid.NewGuid();
    }

    private ScriptProgressState[]? _chain;
    public ScriptProgressState[]? Chain {
        get => _chain;
        set {
            _chain = value;
            Base?.ProgressChanged(this, chainChanged: true);
        }
    }

    public Guid Id { get; init; }

    private string _text = "";
    public string Text {
        get => _text;
        set {
            _text = value;
            this.Base?.ProgressChanged(this);
        }
    }

    private float? _progress;

    public float? Progress {
        get => _progress;
        set {
            _progress = value;
            this.Base?.ProgressChanged(this);
        }
    }

    private TASK_STATE _state = TASK_STATE.WAITING;
    public TASK_STATE State {
        get => _state;
        set {
            _state = value;
            this.Base?.ProgressChanged(this);
        }
    }
    
    private string? _stateData = null;
    
    public string? StateData {
        get => _stateData;
        set {
            _stateData = value;
            this.Base?.ProgressChanged(this);
        }
    }
}
