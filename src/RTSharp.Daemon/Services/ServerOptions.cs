namespace RTSharp.Daemon.Services;

public class ServerOptions
{
    public ulong? DLBandwidth { get; set; }

    public ulong? UPBandwidth { get; set; }

    public string? ScriptStorePath { get; set; }

    public string? DatabasePath { get; set; }
}
