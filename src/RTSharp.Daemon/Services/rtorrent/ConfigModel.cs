namespace RTSharp.Daemon.Services.rtorrent;

public record ConfigModel
{
    public required string SCGIListen { get; init; }
}