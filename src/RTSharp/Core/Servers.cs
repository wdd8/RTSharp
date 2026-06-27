using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using RTSharp.Core.Services.Daemon;

using System.Collections.Concurrent;

namespace RTSharp.Core;

public static class Servers
{
    public static ConcurrentDictionary<string, DaemonService> All { get; } = new();

    public static void Add(string serverId, Config.Models.Server serverConfig)
    {
        var channel = ConfigureServices.CreateChannel(serverId, serverConfig);
        var lifetime = ServiceProvider._provider.GetRequiredService<IHostApplicationLifetime>();
        var daemonService = new DaemonService(serverId, serverConfig, channel, lifetime);
        All[serverId] = daemonService;

        var renderer = new ServersActionQueueRenderer(serverId, daemonService);
        ActionQueue.RegisterActionQueue(daemonService, renderer);
        _ = renderer.TrackServerActions(daemonService.CancellationToken);
    }

    public static void Remove(string serverId)
    {
        if (All.TryRemove(serverId, out var daemonService)) {
            ActionQueue.UnregisterActionQueue(daemonService);
            daemonService.Shutdown();
        }
    }
}
