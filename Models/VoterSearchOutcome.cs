namespace VoterVerification.Models;

public sealed class VoterSearchOutcome
{
    public required IReadOnlyList<VoterSearchHit> Hits { get; init; }

    public bool Truncated { get; init; }

    public bool ScanLimitReached { get; init; }
}
