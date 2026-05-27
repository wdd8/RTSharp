namespace RTSharp.Daemon.Services.transmission;

public record ConfigModel
{
    public required string Uri { get; init; }

    public required string Username { get; init; }

    public required string Password { get; init; }

    public required string? ConfigDir { get; set; }
}