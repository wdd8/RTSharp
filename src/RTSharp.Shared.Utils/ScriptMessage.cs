using MessagePack;
using MessagePack.Resolvers;

using System.Collections;

namespace RTSharp.Shared.Utils;

/// <summary>
/// Message on a script session channel
/// </summary>
/// <param name="Type">Full name of the message type</param>
/// <param name="Payload">MessagePack payload</param>
public readonly record struct ScriptMessage(string Type, ReadOnlyMemory<byte> Payload)
{
    public static readonly MessagePackSerializerOptions SerializerOptions = MessagePackSerializerOptions.Standard
        .WithResolver(ContractlessStandardResolver.Instance)
        .WithSecurity(MessagePackSecurity.UntrustedData);

    /// <summary>
    /// Encodes <paramref name="Value" />
    /// </summary>
    public static ScriptMessage Of<T>(T Value) where T : IScriptMessage<T>
        => new(typeof(T).FullName!, MessagePackSerializer.Serialize(Value.ToMessage(), SerializerOptions));

    /// <summary>
    /// Decodes message from current <see cref="Payload" />
    /// </summary>
    public T Unpack<T>() where T : IScriptMessage<T>
    {
        if (Type != typeof(T).FullName)
            throw new InvalidCastException($"Message carries a '{Type}', not a '{typeof(T).FullName}'");

        return T.FromMessage(MessagePackSerializer.Deserialize<Dictionary<string, object?>>(Payload, SerializerOptions));
    }

    /// <summary>
    /// Decodes message from current <see cref="Payload" />
    /// </summary>
    public bool TryUnpack<T>(out T Value) where T : IScriptMessage<T>
    {
        if (Type != typeof(T).FullName) {
            Value = default!;
            return false;
        }

        Value = Unpack<T>();
        return true;
    }

    public static Dictionary<string, object?> Nested(object? Value)
    {
        var ret = new Dictionary<string, object?>();

        foreach (DictionaryEntry entry in (IDictionary)Value!)
            ret[(string)entry.Key] = entry.Value;

        return ret;
    }

    public override string ToString() => $"{Type} ({Payload.Length} B)";
}
