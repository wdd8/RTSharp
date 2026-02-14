namespace RTSharp.Shared.Abstractions.Daemon;

public interface IDaemonStatsService
{
    Task<AllTimeDataStats> GetAllTimeDataStats(CancellationToken cancellationToken);
}
