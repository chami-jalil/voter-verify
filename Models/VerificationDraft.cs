namespace VoterVerification.Models;

public sealed class VerificationDraft
{
    public required string WardId { get; init; }

    public required string SerialNumber { get; init; }

    public required string PhoneNumber { get; init; }

    public required bool IsVerified { get; init; }

    public required bool HasExistingRow { get; init; }

    public string? ETag { get; init; }

    public bool WasVerified { get; init; }

    public DateTimeOffset? PreviousVerifiedAtUtc { get; init; }
}
