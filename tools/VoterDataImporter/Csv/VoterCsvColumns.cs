namespace VoterDataImporter.Csv;

public static class VoterCsvColumns
{
    public const string WardId = "WardId";
    public const string WardVoterSerialNumber = "WardVoterSerialNumber";
    public const string EpicNumber = "EpicNumber";
    public const string AssemblyRollReference = "AssemblyRollReference";
    public const string NameTelugu = "NameTelugu";
    public const string NameEnglish = "NameEnglish";
    public const string RelativeNameTelugu = "RelativeNameTelugu";
    public const string RelativeNameEnglish = "RelativeNameEnglish";
    public const string Age = "Age";
    public const string Gender = "Gender";
    public const string HouseNumber = "HouseNumber";
    public const string NormalizedHouseNumber = "NormalizedHouseNumber";
    public const string SectionNumber = "SectionNumber";

    public static readonly string[] All =
    [
        WardId,
        WardVoterSerialNumber,
        EpicNumber,
        AssemblyRollReference,
        NameTelugu,
        NameEnglish,
        RelativeNameTelugu,
        RelativeNameEnglish,
        Age,
        Gender,
        HouseNumber,
        NormalizedHouseNumber,
        SectionNumber
    ];
}
