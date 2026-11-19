using VoterVerification.Models;

namespace VoterVerification.Helpers;

public static class VerificationUpdatePolicy
{
    public static VerificationWritePlan Create(VerificationDraft draft, SignedInUser user, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(user);

        if (draft.HasExistingRow)
        {
            if (string.IsNullOrWhiteSpace(draft.ETag) || draft.ETag == "*")
            {
                throw new InvalidOperationException("An existing verification cannot be saved without its ETag.");
            }
        }

        DateTimeOffset? verifiedAt = null;
        if (draft.IsVerified)
        {
            verifiedAt = draft.WasVerified && draft.PreviousVerifiedAtUtc is not null
                ? draft.PreviousVerifiedAtUtc
                : utcNow;
        }

        return new VerificationWritePlan
        {
            Insert = !draft.HasExistingRow,
            IfMatchETag = draft.HasExistingRow ? draft.ETag : null,
            PhoneNumber = draft.PhoneNumber,
            IsVerified = draft.IsVerified,
            VerifiedAtUtc = verifiedAt,
            VerifiedBy = user.VerifiedBy,
            UpdatedAtUtc = utcNow
        };
    }
}
