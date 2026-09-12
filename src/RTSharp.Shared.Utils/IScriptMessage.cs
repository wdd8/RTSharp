namespace RTSharp.Shared.Utils;

/// <summary>
/// A script channel message type
/// </summary>
public interface IScriptMessage<TSelf> where TSelf : IScriptMessage<TSelf>
{
    /// <summary>
    /// Puts each property as a dictionary KV
    /// </summary>
    Dictionary<string, object?> ToMessage();

    /// <summary>
    /// Rebuilds a message from what <see cref="ToMessage"/> produced
    /// </summary>
    static abstract TSelf FromMessage(Dictionary<string, object?> Message);
}
