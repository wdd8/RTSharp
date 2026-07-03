namespace RTSharp.Daemon.Services.qbittorrent;

public record ConfigModel
{
    public required string Uri { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    /// <summary>
    /// API key for qBittorrent 5.2.0 or later. When set, it is used instead of
    /// <see cref="Username"/>/<see cref="Password"/> authentication.
    /// </summary>
    public string? ApiKey { get; init; }
}
