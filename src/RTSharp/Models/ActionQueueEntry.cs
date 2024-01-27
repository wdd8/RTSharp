using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;

namespace RTSharp.Models;

public record ActionQueueEntry(PluginInstance Plugin, IActionQueue Queue);