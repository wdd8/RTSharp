using System.Runtime.CompilerServices;

[assembly:InternalsVisibleTo("RTSharp")]

namespace RTSharp.Shared.Abstractions;

internal static class Consts {
    internal static string PLUGINS_PATH = "plugins";
    internal static string USER_DATA_PATH = "user";
    internal static string PLUGINS_CONFIG_PATH = Path.Combine(USER_DATA_PATH, "plugins");
    internal static string APP_CONFIG_PATH = Path.Combine(USER_DATA_PATH, "config.json");
}