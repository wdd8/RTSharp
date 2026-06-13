using System.Runtime.CompilerServices;

[assembly:InternalsVisibleTo("RTSharp")]

namespace RTSharp.Shared.Abstractions;

internal static class Consts {
    internal static readonly string PLUGINS_PATH = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
    internal static readonly string USER_DATA_PATH = ResolveUserDataPath();
    internal static readonly string PLUGINS_CONFIG_PATH = Path.Combine(USER_DATA_PATH, "plugins");
    internal static readonly string APP_CONFIG_PATH = Path.Combine(USER_DATA_PATH, "config.json");

    private static string ResolveUserDataPath()
    {
        if (OperatingSystem.IsLinux()) {
            var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(xdgConfigHome))
                xdgConfigHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(xdgConfigHome, "rtsharp");
        }
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user");
    }
}