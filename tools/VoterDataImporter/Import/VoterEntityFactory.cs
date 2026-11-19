using VoterVerification.Data;
using VoterVerification.Helpers;

namespace VoterDataImporter.Import;

public static class VoterEntityFactory
{
    public static VoterTableEntity Create(
        string wardId,
        string serialNumber,
        string epicNumber,
        string assemblyRollReference,
        string nameTelugu,
        string nameEnglish,
        string relativeNameTelugu,
        string relativeNameEnglish,
        int age,
        string gender,
        string houseNumber,
        string normalizedHouseNumber,
        string sectionNumber)
    {
        if (!TableKeyRules.IsValid(wardId) || !TableKeyRules.IsValid(serialNumber))
        {
            throw new InvalidOperationException("PartitionKey and RowKey must pass the table key rules.");
        }

        return new VoterTableEntity
        {
            PartitionKey = wardId,
            RowKey = serialNumber,
            WardId = wardId,
            WardVoterSerialNumber = serialNumber,
            EpicNumber = epicNumber,
            AssemblyRollReference = assemblyRollReference,
            NameTelugu = nameTelugu,
            NameEnglish = nameEnglish,
            RelativeNameTelugu = relativeNameTelugu,
            RelativeNameEnglish = relativeNameEnglish,
            Age = age,
            Gender = gender,
            HouseNumber = houseNumber,
            NormalizedHouseNumber = normalizedHouseNumber,
            SectionNumber = sectionNumber
        };
    }
}
