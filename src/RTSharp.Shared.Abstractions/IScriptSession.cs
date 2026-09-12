using RTSharp.Shared.Utils;

using System.Threading.Channels;

namespace RTSharp.Shared.Abstractions;

public interface IScriptSession
{
    public void ProgressChanged(ScriptProgressState? progress = null, bool chainChanged = false);

    public ScriptProgressState Progress { get; }

    /// <summary>
    /// Message RX by <typeparamref name="T"/> type
    /// </summary>
    /// <exception cref="ChannelClosedException">The session ended before one did</exception>
    public ValueTask<T> Rx<T>(CancellationToken CancellationToken) where T : IScriptMessage<T>;

    /// <summary>
    /// Message TX
    /// </summary>
    public void Send<T>(T Message) where T : IScriptMessage<T>;
}
