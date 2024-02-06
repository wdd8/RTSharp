using System;
using System.Collections.ObjectModel;
using System.Linq;
using RTSharp.Models;
using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;

namespace RTSharp.Core
{
    public static class ActionQueue
    {
        public static ObservableCollection<ActionQueueEntry> ActionQueues { get; } = new ObservableCollection<ActionQueueEntry>();

        private static object ActionQueueLock = new object();
        public static void RegisterActionQueue(PluginInstance Plugin, IActionQueue In)
        {
            lock (ActionQueueLock) {
                var found = GetActionQueueEntry(Plugin);

                if (found != default)
                    throw new ArgumentException("IActionQueue has already been registered by this plugin");

                ActionQueues.Add(new ActionQueueEntry(Plugin, In));
            }
        }

        public static void UnregisterActionQueue(PluginInstance Plugin)
        {
            lock (ActionQueueLock) {
                var found = GetActionQueueEntry(Plugin);

                if (found == default)
                    throw new ArgumentException("No IActionQueue by this plugin");

                ActionQueues.Remove(found);
            }
        }

        public static ActionQueueEntry? GetActionQueueEntry(PluginInstance Plugin)
        {
            lock (ActionQueueLock) {
                return ActionQueues.FirstOrDefault(x => x.Plugin == Plugin);
            }
        }
    }
}
