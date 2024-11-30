#nullable enable

namespace RTSharp.Shared.Abstractions;

public enum TASK_STATE
{
    WAITING,
    RUNNING,
    DONE
}

public class ScriptProgressState
{
    private readonly IScriptSession Base;

    public ScriptProgressState(IScriptSession Base)
    {
        this.Base = Base;
    }

    public ScriptProgressState[]? Chain { get; set; }

    private string _text = "";
    public string Text {
        get => _text;
        set {
            _text = value;
            this.Base.ProgressChanged();
        }
    }

    private float? _progress;

    public float? Progress {
        get => _progress;
        set {
            _progress = value;
            this.Base.ProgressChanged();
        }
    }

    private TASK_STATE _state = TASK_STATE.WAITING;
    public TASK_STATE State {
        get => _state;
        set {
            _state = value;
            this.Base.ProgressChanged();
        }
    }
}
