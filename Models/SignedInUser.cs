namespace VoterVerification.Models;

public sealed class SignedInUser
{
    public required string ObjectId { get; init; }

    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Value stored in VoterVerifications.VerifiedBy.</summary>
    public required string VerifiedBy { get; init; }
}
