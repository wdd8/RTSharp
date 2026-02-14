namespace RTSharp.Shared.Abstractions;

public record ListingChanges<TFullUpdate, TChanged, TRemoved>(IEnumerable<TFullUpdate> FullUpdate, IEnumerable<TChanged> Changes, IEnumerable<TRemoved> Removed);
