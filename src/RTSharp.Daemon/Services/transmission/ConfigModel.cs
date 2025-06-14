namespace RTSharp.Daemon.Services.transmission;

public class ConfigModel
{
    public string Uri { get; init; }

    public string Username { get; init; }

    public string Password { get; init; }

    public string? ConfigDir { get; set; }
}