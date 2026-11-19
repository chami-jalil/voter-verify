namespace VoterVerification.Models;

public sealed class VoterRecord
{
    public required string WardId { get; init; }

    public required string WardVoterSerialNumber { get; init; }

    public string EpicNumber { get; init; } = "";

    public string AssemblyRollReference { get; init; } = "";

    public string NameTelugu { get; init; } = "";

    public string NameEnglish { get; init; } = "";

    public string RelativeNameTelugu { get; init; } = "";

    public string RelativeNameEnglish { get; init; } = "";

    public int Age { get; init; }

    public string Gender { get; init; } = "";

    public string HouseNumber { get; init; } = "";

    public string NormalizedHouseNumber { get; init; } = "";

    public string SectionNumber { get; init; } = "";
}
