using Azure;
using Azure.Data.Tables;

namespace VoterVerification.Data;

public sealed class VoterTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";

    public string RowKey { get; set; } = "";

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string WardId { get; set; } = "";

    public string WardVoterSerialNumber { get; set; } = "";

    public string EpicNumber { get; set; } = "";

    public string AssemblyRollReference { get; set; } = "";

    public string NameTelugu { get; set; } = "";

    public string NameEnglish { get; set; } = "";

    public string RelativeNameTelugu { get; set; } = "";

    public string RelativeNameEnglish { get; set; } = "";

    public int Age { get; set; }

    public string Gender { get; set; } = "";

    public string HouseNumber { get; set; } = "";

    public string NormalizedHouseNumber { get; set; } = "";

    public string SectionNumber { get; set; } = "";
}
