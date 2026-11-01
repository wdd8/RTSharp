namespace RTSharp.Scripts.PeerReputation;

public enum PeerReputationCommand
{
    GetAsnList
}

public partial record PeerReputationCommandRequest(int Command);
public partial record PeerAsnListResult(PeerAsnInfo[] PeerAsns);
public partial record PeerAsnInfo(
    uint PeerAsn,
    ulong AverageDownloadSpeed,
    ulong AverageUploadSpeed,
    ulong PeerOccurrenceCount,
    TrackerDistributionInfo[] TrackerDistribution);
public partial record TrackerDistributionInfo(string Tracker, ulong PeerCount);
