namespace RTSharp.Shared.Abstractions;

/// <summary>
/// A script installed on a server, which the server starts again every time it launches
/// </summary>
public record InstalledScript
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// The running session to which commands can be sent, if the script is active
    /// </summary>
    public Guid? SessionId { get; init; }

    /// <summary>
    /// GUID of the plugin the script belongs to, or <see cref="Guid.Empty"/> for the application
    /// itself
    /// </summary>
    public required Guid OwnerId { get; init; }

    public required byte[] ScriptHash { get; init; }

    public required bool OrphanAcknowledged { get; init; }

    public required TASK_STATE State { get; init; }

    public required string StatusText { get; init; }

    public required string? LastError { get; init; }

    public required DateTimeOffset InstalledAt { get; init; }

    public required DateTimeOffset? StartedAt { get; init; }
}
