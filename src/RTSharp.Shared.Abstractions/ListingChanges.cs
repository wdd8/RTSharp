namespace RTSharp.Shared.Abstractions;

public record ListingChanges<TFullUpdate, TChanged, TRemoved>(IList<TFullUpdate> FullUpdate, IList<TChanged> Changes, IList<TRemoved> Removed);
