using RTSharp.Core.Services.Daemon;

using System.Collections.Generic;

namespace RTSharp.Core;

public static class Servers
{
    public static Dictionary<string, DaemonService> Value { get; } = new();
}
