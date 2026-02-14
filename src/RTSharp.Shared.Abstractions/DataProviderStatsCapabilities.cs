namespace RTSharp.Shared.Abstractions
{
    public record DataProviderStatsCapabilities(
        bool GetStateHistory,
        bool GetAllTimeDataStats
    );
}
