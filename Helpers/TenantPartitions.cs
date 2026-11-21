using VoterVerification.Data;

namespace VoterVerification.Helpers;

/// <summary>
/// Customer tenant ids are application accounts, not Entra directory ids.
/// Existing ward rows stay at PartitionKey = ward id. New tenant rows use
/// "{tenantId}|{wardId}" so two customers cannot share a record key.
/// </summary>
public static class TenantPartitions
{
    public const char Separator = '|';

    public static bool IsCustomerTenantId(string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 64 || tenantId.Contains(Separator))
        {
            return false;
        }

        return TableKeyRules.IsValid(tenantId);
    }

    public static string For(string tenantId, string wardId)
    {
        if (!IsCustomerTenantId(tenantId) || !TableKeyRules.IsValid(wardId))
        {
            throw new InvalidOperationException("The customer tenant id and ward id cannot be used as a table key.");
        }

        return tenantId + Separator + wardId;
    }

    public static void ApplyCustomerTenant(VoterTableEntity entity, string tenantId)
    {
        var wardId = string.IsNullOrWhiteSpace(entity.WardId) ? entity.PartitionKey : entity.WardId;
        entity.WardId = wardId;
        entity.TenantId = tenantId;
        entity.PartitionKey = For(tenantId, wardId);
    }

    public static IReadOnlyList<string> ReadKeys(string tenantId, string wardId, string? initialTenantId)
    {
        var keys = new List<string> { For(tenantId, wardId) };
        if (IsInitialTenant(tenantId, initialTenantId) && TableKeyRules.IsValid(wardId))
        {
            keys.Add(wardId);
        }

        return keys;
    }

    public static bool IsInitialTenant(string tenantId, string? initialTenantId) =>
        !string.IsNullOrWhiteSpace(initialTenantId)
        && string.Equals(tenantId, initialTenantId, StringComparison.Ordinal);

    public static IReadOnlyList<VoterTableEntity> VisibleVoters(
        string tenantId,
        string wardId,
        string? initialTenantId,
        IReadOnlyDictionary<string, IReadOnlyList<VoterTableEntity>> rowsByPartition)
    {
        var visible = new List<VoterTableEntity>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var partition in ReadKeys(tenantId, wardId, initialTenantId))
        {
            if (!rowsByPartition.TryGetValue(partition, out var rows))
            {
                continue;
            }

            foreach (var row in rows)
            {
                if (!CanRead(row.PartitionKey, row.TenantId, row.WardId, row.RowKey, tenantId, wardId, initialTenantId))
                {
                    continue;
                }

                var serial = string.IsNullOrWhiteSpace(row.WardVoterSerialNumber) ? row.RowKey : row.WardVoterSerialNumber;
                if (seen.Add(serial))
                {
                    visible.Add(row);
                }
            }
        }

        return visible;
    }

    public static bool CanRead(
        string? partitionKey,
        string? rowTenantId,
        string? rowWardId,
        string? rowKey,
        string callerTenantId,
        string wardId,
        string? initialTenantId)
    {
        if (string.IsNullOrWhiteSpace(partitionKey))
        {
            return false;
        }

        var serial = rowKey ?? "";
        if (!TableKeyRules.IsValid(serial))
        {
            return false;
        }

        var wardOnRow = string.IsNullOrWhiteSpace(rowWardId) ? WardFromPartition(partitionKey) : rowWardId;
        if (!string.Equals(wardOnRow, wardId, StringComparison.Ordinal))
        {
            return false;
        }

        var tenantPartition = For(callerTenantId, wardId);
        var storedTenant = rowTenantId ?? "";
        if (string.Equals(partitionKey, tenantPartition, StringComparison.Ordinal))
        {
            return storedTenant.Length == 0 || string.Equals(storedTenant, callerTenantId, StringComparison.Ordinal);
        }

        if (IsInitialTenant(callerTenantId, initialTenantId)
            && string.Equals(partitionKey, wardId, StringComparison.Ordinal))
        {
            return storedTenant.Length == 0 || string.Equals(storedTenant, callerTenantId, StringComparison.Ordinal);
        }

        return false;
    }

    public static string? PartitionForSerial(IEnumerable<VoterTableEntity> visible, string serialNumber)
    {
        foreach (var row in visible)
        {
            var serial = string.IsNullOrWhiteSpace(row.WardVoterSerialNumber) ? row.RowKey : row.WardVoterSerialNumber;
            if (string.Equals(serial, serialNumber, StringComparison.Ordinal))
            {
                return row.PartitionKey;
            }
        }

        return null;
    }

    private static string WardFromPartition(string partitionKey)
    {
        var separator = partitionKey.LastIndexOf(Separator);
        return separator > 0 && separator < partitionKey.Length - 1
            ? partitionKey[(separator + 1)..]
            : partitionKey;
    }
}
