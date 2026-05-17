#nullable enable

namespace RTSharp.Shared.Abstractions;

public class ScriptSessionState
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required ScriptProgressState Progress { get; init; }
}

public class ScriptSessionStateUpdate
{
    public required bool FullUpdate { get; init; }

    public ScriptSessionState[] Sessions { get; init; } = [];

    public Guid SessionId { get; init; }

    public Guid? ParentStateId { get; init; }

    public ScriptProgressState? State { get; init; }
}
