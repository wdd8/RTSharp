using Google.Protobuf;

using RTSharp.Shared.Utils;

using ProtocolScriptMessage = RTSharp.Daemon.Protocols.ScriptMessage;

namespace RTSharp.Shared.Abstractions;

public static class ScriptMessageExtensions
{
    public static ProtocolScriptMessage ToProtocol(this ScriptMessage In) => new() {
        Type = In.Type,
        Payload = UnsafeByteOperations.UnsafeWrap(In.Payload)
    };

    public static ScriptMessage ToScriptMessage(this ProtocolScriptMessage In) => new(In.Type, In.Payload.Memory);
}
