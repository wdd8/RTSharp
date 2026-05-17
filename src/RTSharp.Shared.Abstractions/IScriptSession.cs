namespace RTSharp.Shared.Abstractions;

public interface IScriptSession
{
    public void ProgressChanged(ScriptProgressState? progress = null, bool chainChanged = false);

    public ScriptProgressState Progress { get; }
}
