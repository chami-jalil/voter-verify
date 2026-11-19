namespace VoterVerification.Models;

public sealed class VoterVerificationRecord
{
    public required string WardId { get; init; }

    public required string WardVoterSerialNumber { get; init; }

    public string PhoneNumber { get; init; } = "";

    public bool IsVerified { get; init; }

    public DateTimeOffset? VerifiedAtUtc { get; init; }

    public string VerifiedBy { get; init; } = "";

    public DateTimeOffset? UpdatedAtUtc { get; init; }

    /// <summary>Entity tag from Azure Table Storage. Required for a conditional update.</summary>
    public string? ETag { get; init; }
}
