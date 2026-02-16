using System;
using System.Collections.ObjectModel;
using System.Linq;

using RTSharp.Core.Services.Daemon;
using RTSharp.Models;
using RTSharp.Plugin;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.Core;

public static class ActionQueue
{
    public static ObservableCollection<ActionQueueEntry> ActionQueues { get; } = new ObservableCollection<ActionQueueEntry>();

    private static object ActionQueueLock = new object();
    public static void RegisterActionQueue(RTSharpPlugin Plugin, IActionQueueRenderer In)
    {
        lock (ActionQueueLock) {
            var found = GetActionQueueEntry(Plugin);

            if (found != default)
                throw new ArgumentException("IActionQueue has already been registered by this plugin");

            ActionQueues.Add(new ActionQueueEntry(Plugin, In));
        }
    }

    public static void RegisterActionQueue(DaemonService Server, IActionQueueRenderer In)
    {
        lock (ActionQueueLock) {
            var found = GetActionQueueEntry(Server);

            if (found != default)
                throw new ArgumentException("IActionQueue has already been registered by this server");

            ActionQueues.Add(new ActionQueueEntry(Server, In));
        }
    }

    public static void UnregisterActionQueue(DaemonService Server)
    {
        lock (ActionQueueLock) {
            var found = GetActionQueueEntry(Server);

            if (found == default)
                throw new ArgumentException("No IActionQueue by this server");

            ActionQueues.Remove(found);
        }
    }

    public static void UnregisterActionQueue(RTSharpPlugin Plugin)
    {
        lock (ActionQueueLock) {
            var found = GetActionQueueEntry(Plugin);

            if (found == default)
                throw new ArgumentException("No IActionQueue by this plugin");

            ActionQueues.Remove(found);
        }
    }

    public static ActionQueueEntry? GetActionQueueEntry(object Related)
    {
        lock (ActionQueueLock) {
            return ActionQueues.FirstOrDefault(x => x.Related == Related);
        }
    }
}
