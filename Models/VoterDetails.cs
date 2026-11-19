namespace VoterVerification.Models;

public sealed class VoterDetails
{
    public required VoterRecord Voter { get; init; }

    public VoterVerificationRecord? Verification { get; init; }
}
