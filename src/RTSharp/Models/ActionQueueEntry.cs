using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;

namespace RTSharp.Models;

public record ActionQueueEntry(object Related, IActionQueueRenderer Queue);