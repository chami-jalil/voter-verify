namespace VoterVerification.Models;

public sealed class VerificationWritePlan
{
    public required bool Insert { get; init; }

    public string? IfMatchETag { get; init; }

    public required string PhoneNumber { get; init; }

    public required bool IsVerified { get; init; }

    public DateTimeOffset? VerifiedAtUtc { get; init; }

    public required string VerifiedBy { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}
