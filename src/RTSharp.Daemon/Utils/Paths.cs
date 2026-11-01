namespace RTSharp.Daemon.Utils;

public static class Paths
{
    public static string GetConfigPath()
    {
        if (OperatingSystem.IsLinux()) {
            const string etcPath = "/etc/rtsharp-daemon";
            if (Directory.Exists(etcPath))
                return etcPath;
        }
        return AppContext.BaseDirectory;
    }

    public static string ResolveStatePath(string? ConfiguredPath, string ContentRootPath, string DefaultPath)
    {
        if (!String.IsNullOrWhiteSpace(ConfiguredPath))
            return Path.GetFullPath(ConfiguredPath, ContentRootPath);

        return Path.Combine(GetStateDirectory(), DefaultPath);
    }

    public static string GetStateDirectory()
    {
        var stateDirectory = Environment.GetEnvironmentVariable("STATE_DIRECTORY")?.Split(':', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        return String.IsNullOrEmpty(stateDirectory) ? AppContext.BaseDirectory : stateDirectory;
    }
}
