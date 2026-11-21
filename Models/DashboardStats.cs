namespace VoterVerification.Models;

public sealed class DashboardStats
{
    public int VotersOnFile { get; init; }

    public int VerifiedCount { get; init; }

    public int? ExpectedVoters { get; init; }
}
