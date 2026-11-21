using VoterVerification.Models;

namespace VoterVerification.Data;

public static class TableEntityMapper
{
    public static VoterRecord ToRecord(VoterTableEntity entity)
    {
        var wardId = string.IsNullOrWhiteSpace(entity.WardId) ? entity.PartitionKey : entity.WardId;
        var serial = string.IsNullOrWhiteSpace(entity.WardVoterSerialNumber) ? entity.RowKey : entity.WardVoterSerialNumber;
        return new VoterRecord
        {
            WardId = wardId,
            WardVoterSerialNumber = serial,
            EpicNumber = entity.EpicNumber ?? "",
            AssemblyRollReference = entity.AssemblyRollReference ?? "",
            NameTelugu = entity.NameTelugu ?? "",
            NameEnglish = entity.NameEnglish ?? "",
            RelativeNameTelugu = entity.RelativeNameTelugu ?? "",
            RelativeNameEnglish = entity.RelativeNameEnglish ?? "",
            Age = entity.Age,
            Gender = entity.Gender ?? "",
            HouseNumber = entity.HouseNumber ?? "",
            NormalizedHouseNumber = entity.NormalizedHouseNumber ?? "",
            SectionNumber = entity.SectionNumber ?? ""
        };
    }

    public static VoterSearchHit ToHit(VoterTableEntity entity)
    {
        var record = ToRecord(entity);
        return new VoterSearchHit
        {
            SerialNumber = record.WardVoterSerialNumber,
            NameTelugu = record.NameTelugu,
            NameEnglish = record.NameEnglish,
            RelativeEnglish = record.RelativeNameEnglish,
            EpicNumber = record.EpicNumber,
            HouseNumber = record.HouseNumber,
            Age = record.Age,
            Gender = record.Gender
        };
    }

    public static VoterVerificationRecord ToRecord(VoterVerificationTableEntity entity) =>
        new()
        {
            WardId = string.IsNullOrWhiteSpace(entity.WardId) ? entity.PartitionKey : entity.WardId,
            WardVoterSerialNumber = entity.RowKey,
            PhoneNumber = entity.PhoneNumber ?? "",
            IsVerified = entity.IsVerified,
            VerifiedAtUtc = entity.VerifiedAtUtc,
            VerifiedBy = entity.VerifiedBy ?? "",
            UpdatedAtUtc = entity.UpdatedAtUtc,
            ETag = entity.ETag.ToString()
        };

    public static VoterVerificationTableEntity ToEntity(string wardId, string serialNumber, VerificationWritePlan plan) =>
        new()
        {
            PartitionKey = wardId,
            RowKey = serialNumber,
            PhoneNumber = plan.PhoneNumber,
            IsVerified = plan.IsVerified,
            VerifiedAtUtc = plan.VerifiedAtUtc,
            VerifiedBy = plan.VerifiedBy,
            UpdatedAtUtc = plan.UpdatedAtUtc
        };
}
