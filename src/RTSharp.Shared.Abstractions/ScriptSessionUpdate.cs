namespace RTSharp.Shared.Abstractions;

public class ScriptSessionUpdate
{
    public required Guid SessionId { get; init; }

    public string? SessionName { get; init; }

    public Guid? ParentStateId { get; init; }

    public required ScriptProgressState State { get; init; }
}
