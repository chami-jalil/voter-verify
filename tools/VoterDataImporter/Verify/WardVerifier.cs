using System.Text;
using VoterDataImporter.Validation;
using VoterVerification.Data;
using VoterVerification.Helpers;

namespace VoterDataImporter.Verify;

public sealed class WardCheckResult
{
    public required string WardId { get; init; }

    public required int Expected { get; init; }

    public required int Total { get; init; }

    public required int DuplicateSerialNumbers { get; init; }

    public required int DuplicateEpicNumbers { get; init; }

    public required int InvalidRows { get; init; }

    public required int MissingFields { get; init; }

    public required int KeyMismatches { get; init; }

    public required IReadOnlyList<string> Problems { get; init; }

    public required IReadOnlyList<VoterTableEntity> Samples { get; init; }

    public bool CountMatches => Total == Expected;

    public bool IsSuccessful =>
        CountMatches
        && DuplicateSerialNumbers == 0
        && DuplicateEpicNumbers == 0
        && InvalidRows == 0
        && MissingFields == 0
        && KeyMismatches == 0;

    public string Format()
    {
        var text = new StringBuilder();
        text.AppendLine($"Ward {WardId} stored records: {Total}");
        text.AppendLine($"Expected records: {Expected}");
        text.AppendLine($"Duplicate serial numbers: {DuplicateSerialNumbers}");
        text.AppendLine($"Duplicate EPIC numbers: {DuplicateEpicNumbers}");
        text.AppendLine($"Rows with missing or invalid fields: {InvalidRows}");
        text.AppendLine($"Missing fields: {MissingFields}");
        text.AppendLine($"Key mismatches: {KeyMismatches}");
        text.AppendLine(IsSuccessful
            ? "Verification passed. The stored Voters rows match the expected ward."
            : "Verification failed. A matching CSV row count is not treated as success.");
        foreach (var problem in Problems)
        {
            text.AppendLine(problem);
        }

        text.AppendLine("Sample records:");
        if (Samples.Count == 0)
        {
            text.AppendLine("(none)");
        }

        foreach (var sample in Samples)
        {
            text.AppendLine(
                $"{sample.RowKey} | {sample.EpicNumber} | {sample.NameTelugu} | {sample.NameEnglish} | {sample.HouseNumber} | {sample.Age} | {sample.Gender} | {sample.AssemblyRollReference}");
        }

        return text.ToString();
    }
}

public static class WardVerifier
{
    public static WardCheckResult Check(IReadOnlyList<VoterTableEntity> stored, string wardId, int expected)
    {
        var problems = new List<string>();
        var keyMismatches = 0;
        foreach (var entity in stored)
        {
            if (!string.Equals(entity.PartitionKey, wardId, StringComparison.Ordinal)
                || !string.Equals(entity.WardId, wardId, StringComparison.Ordinal)
                || !string.Equals(entity.RowKey, entity.WardVoterSerialNumber, StringComparison.Ordinal)
                || !TableKeyRules.IsValid(entity.PartitionKey)
                || !TableKeyRules.IsValid(entity.RowKey))
            {
                keyMismatches++;
                problems.Add($"Key mismatch for RowKey '{entity.RowKey}'.");
            }
        }

        var duplicateSerials = stored
            .Where(entity => !string.IsNullOrWhiteSpace(entity.WardVoterSerialNumber))
            .GroupBy(entity => entity.WardVoterSerialNumber, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToList();
        foreach (var group in duplicateSerials)
        {
            problems.Add($"Serial {group.Key} is stored {group.Count()} times.");
        }

        var report = VoterCsvValidator.ValidateEntities(stored, wardId);
        if (stored.Count != expected)
        {
            problems.Add($"Stored count {stored.Count} does not equal expected {expected}.");
        }

        foreach (var finding in report.Findings.Where(item => item.Severity == FindingSeverity.Error))
        {
            problems.Add($"Stored record {finding.RowNumber}: {finding.Message}");
        }

        return new WardCheckResult
        {
            WardId = wardId,
            Expected = expected,
            Total = stored.Count,
            DuplicateSerialNumbers = duplicateSerials.Count,
            DuplicateEpicNumbers = report.DuplicateEpicNumbers,
            InvalidRows = report.InvalidRows,
            MissingFields = report.MissingFields,
            KeyMismatches = keyMismatches,
            Problems = problems,
            Samples = SelectSamples(stored)
        };
    }

    public static IReadOnlyList<VoterTableEntity> SelectSamples(IReadOnlyList<VoterTableEntity> stored)
    {
        var ordered = stored
            .OrderBy(entity => entity.RowKey ?? "", Comparer<string>.Create(VoterTextMatch.CompareSerial))
            .ToList();
        if (ordered.Count <= 5)
        {
            return ordered;
        }

        var middle = ordered.Count / 2;
        return [ordered[0], ordered[1], ordered[middle], ordered[^2], ordered[^1]];
    }
}
