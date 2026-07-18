namespace RTSharp.Daemon.Services.rtorrent;

public record ConfigModel
{
    public required string SCGIListen { get; init; }

    public required string? SessionDir { get; set; }

    public string? LogFile { get; init; }
}