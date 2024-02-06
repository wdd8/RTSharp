namespace RTSharp.Shared.Abstractions
{
    public record DataProviderTrackerCapabilities(
        bool AddNewTracker,
        bool EnableTracker,
        bool DisableTracker,
        bool RemoveTracker,
        bool ReannounceTracker
    );
}
