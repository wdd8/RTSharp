using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.Models;

public record ActionQueueEntry(object Related, IActionQueueRenderer Queue);