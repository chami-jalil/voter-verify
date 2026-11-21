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

    public required int InvalidPartitionKeys { get; init; }

    public required int InvalidRowKeys { get; init; }

    public required int KeyMismatches { get; init; }

    public required IReadOnlyList<string> Problems { get; init; }

    public bool CountMatches => Total == Expected;

    public bool IsSuccessful =>
        CountMatches
        && DuplicateSerialNumbers == 0
        && DuplicateEpicNumbers == 0
        && InvalidRows == 0
        && MissingFields == 0
        && InvalidPartitionKeys == 0
        && InvalidRowKeys == 0
        && KeyMismatches == 0;

    public string Format()
    {
        var text = new StringBuilder();
        text.AppendLine($"Expected record count: {Expected}");
        text.AppendLine($"Actual stored record count: {Total}");
        text.AppendLine($"Missing required fields: {MissingFields}");
        text.AppendLine($"Duplicate serial count: {DuplicateSerialNumbers}");
        text.AppendLine($"Duplicate EPIC count: {DuplicateEpicNumbers}");
        text.AppendLine($"Invalid PartitionKey count: {InvalidPartitionKeys}");
        text.AppendLine($"Invalid RowKey count: {InvalidRowKeys}");
        text.AppendLine(IsSuccessful
            ? "Verification passed. The stored Voters rows match the expected ward."
            : "Verification failed. A matching CSV row count is not treated as success.");
        return text.ToString();
    }
}

public static class WardVerifier
{
    public static WardCheckResult Check(IReadOnlyList<VoterTableEntity> stored, string wardId, int expected, string? customerTenantId = null)
    {
        var problems = new List<string>();
        var invalidPartitionKeys = 0;
        var invalidRowKeys = 0;
        var keyMismatches = 0;
        var expectedPartition = string.IsNullOrEmpty(customerTenantId) ? wardId : TenantPartitions.For(customerTenantId, wardId);
        foreach (var entity in stored)
        {
            var partitionInvalid = !TableKeyRules.IsValid(entity.PartitionKey)
                || !string.Equals(entity.PartitionKey, expectedPartition, StringComparison.Ordinal)
                || (!string.IsNullOrEmpty(customerTenantId)
                    && !string.Equals(entity.TenantId, customerTenantId, StringComparison.Ordinal));
            var rowInvalid = !TableKeyRules.IsValid(entity.RowKey)
                || !string.Equals(entity.RowKey, entity.WardVoterSerialNumber, StringComparison.Ordinal);
            var wardMismatch = !string.Equals(entity.WardId, wardId, StringComparison.Ordinal);
            if (partitionInvalid)
            {
                invalidPartitionKeys++;
            }

            if (rowInvalid)
            {
                invalidRowKeys++;
            }

            if (partitionInvalid || rowInvalid || wardMismatch)
            {
                keyMismatches++;
            }
        }

        var duplicateSerials = stored
            .Where(entity => !string.IsNullOrWhiteSpace(entity.WardVoterSerialNumber))
            .GroupBy(entity => entity.WardVoterSerialNumber, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToList();

        var report = VoterCsvValidator.ValidateEntities(stored, wardId);
        if (stored.Count != expected)
        {
            problems.Add($"Stored count {stored.Count} does not equal expected {expected}.");
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
            InvalidPartitionKeys = invalidPartitionKeys,
            InvalidRowKeys = invalidRowKeys,
            KeyMismatches = keyMismatches,
            Problems = problems
        };
    }
}
