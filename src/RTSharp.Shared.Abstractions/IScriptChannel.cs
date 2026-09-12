using RTSharp.Shared.Utils;

using System.Threading.Channels;

namespace RTSharp.Shared.Abstractions;

/// <summary>
/// A client's view of a script session's message channel
/// </summary>
public interface IScriptChannel : IAsyncDisposable
{
    /// <summary>
    /// Message RX
    /// </summary>
    public ChannelReader<ScriptMessage> Output { get; }

    /// <summary>
    /// Message RX by <typeparamref name="T"/> type
    /// </summary>
    /// <returns>Null if the session ended without sending one</returns>
    public ValueTask<T?> Read<T>(CancellationToken CancellationToken = default) where T : class, IScriptMessage<T>;

    /// <summary>
    /// Message TX
    /// </summary>
    public ValueTask Send<T>(T Message) where T : IScriptMessage<T>;
}
