using VoterVerification.Data;

namespace VoterDataImporter.Import;

public sealed class ExistingVoterRow
{
    public required VoterTableEntity Incoming { get; init; }

    public required VoterTableEntity Stored { get; init; }
}

public sealed class ImportPlan
{
    public required IReadOnlyList<VoterTableEntity> ToInsert { get; init; }

    public required IReadOnlyList<ExistingVoterRow> AlreadyStored { get; init; }

    public required IReadOnlyList<ExistingVoterRow> DifferentExisting { get; init; }
}

public static class ImportPlanner
{
    public static ImportPlan Plan(IReadOnlyList<VoterTableEntity> incoming, IReadOnlyList<VoterTableEntity> existing)
    {
        var stored = new Dictionary<string, VoterTableEntity>(StringComparer.Ordinal);
        foreach (var entity in existing)
        {
            stored[Key(entity)] = entity;
        }

        var toInsert = new List<VoterTableEntity>();
        var same = new List<ExistingVoterRow>();
        var different = new List<ExistingVoterRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in incoming)
        {
            var key = Key(entity);
            if (!seen.Add(key))
            {
                throw new InvalidOperationException("Validated rows still contain a duplicate PartitionKey and RowKey.");
            }

            if (!stored.TryGetValue(key, out var current))
            {
                toInsert.Add(entity);
            }
            else if (VoterEntityEquality.Same(entity, current))
            {
                same.Add(new ExistingVoterRow { Incoming = entity, Stored = current });
            }
            else
            {
                different.Add(new ExistingVoterRow { Incoming = entity, Stored = current });
            }
        }

        return new ImportPlan
        {
            ToInsert = toInsert,
            AlreadyStored = same,
            DifferentExisting = different
        };
    }

    public static string Key(VoterTableEntity entity) => entity.PartitionKey + "\n" + entity.RowKey;
}

public static class VoterEntityEquality
{
    public static bool Same(VoterTableEntity left, VoterTableEntity right) =>
        left.PartitionKey == right.PartitionKey
        && left.RowKey == right.RowKey
        && left.WardId == right.WardId
        && left.WardVoterSerialNumber == right.WardVoterSerialNumber
        && left.EpicNumber == right.EpicNumber
        && left.AssemblyRollReference == right.AssemblyRollReference
        && left.NameTelugu == right.NameTelugu
        && left.NameEnglish == right.NameEnglish
        && left.RelativeNameTelugu == right.RelativeNameTelugu
        && left.RelativeNameEnglish == right.RelativeNameEnglish
        && left.Age == right.Age
        && left.Gender == right.Gender
        && left.HouseNumber == right.HouseNumber
        && left.NormalizedHouseNumber == right.NormalizedHouseNumber
        && left.SectionNumber == right.SectionNumber;
}
