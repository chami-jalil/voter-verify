using Azure;
using Azure.Data.Tables;

namespace VoterVerification.Data;

public sealed class VoterVerificationTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";

    public string RowKey { get; set; } = "";

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string PhoneNumber { get; set; } = "";

    public bool IsVerified { get; set; }

    public DateTimeOffset? VerifiedAtUtc { get; set; }

    public string VerifiedBy { get; set; } = "";

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
