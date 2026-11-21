using Azure.Data.Tables;
using VoterVerification.Functions.Security;
using VoterVerification.Helpers;
using VoterVerification.Models;

namespace VoterVerification.Functions.Data;

public sealed class AccountStore
{
    private readonly TableClient _customers;
    private readonly TableClient _wards;
    private readonly TableClient _users;
    private readonly TableClient _assignments;

    public AccountStore(TableClient customers, TableClient wards, TableClient users, TableClient assignments)
    {
        _customers = customers;
        _wards = wards;
        _users = users;
        _assignments = assignments;
    }

    public async Task<CustomerAccount?> GetCustomerAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (!TenantPartitions.IsCustomerTenantId(tenantId))
        {
            return null;
        }

        var response = await _customers.GetEntityIfExistsAsync<TableEntity>("customer", tenantId, cancellationToken: cancellationToken);
        if (!response.HasValue || response.Value is null)
        {
            return null;
        }

        var wards = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var ward in _wards.QueryAsync<TableEntity>(
            $"PartitionKey eq '{ODataLiteral.Escape(tenantId)}'",
            cancellationToken: cancellationToken))
        {
            if (IsActive(ward) && TableKeyRules.IsValid(ward.RowKey))
            {
                wards.Add(ward.RowKey);
            }
        }

        return new CustomerAccount
        {
            TenantId = tenantId,
            Name = ReadString(response.Value, "CustomerName"),
            IsActive = IsActive(response.Value),
            WardIds = wards
        };
    }

    public async Task<DirectoryUser?> GetUserAsync(string directoryId, string objectId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(directoryId, out _) || !Guid.TryParse(objectId, out _))
        {
            return null;
        }

        var response = await _users.GetEntityIfExistsAsync<TableEntity>(directoryId, objectId, cancellationToken: cancellationToken);
        if (!response.HasValue || response.Value is null)
        {
            return null;
        }

        var userTenant = BlankToNull(ReadString(response.Value, "TenantId"));
        var wards = new HashSet<string>(StringComparer.Ordinal);
        var assignmentKey = AssignmentPartition(directoryId, objectId);
        await foreach (var assignment in _assignments.QueryAsync<TableEntity>(
            $"PartitionKey eq '{ODataLiteral.Escape(assignmentKey)}'",
            cancellationToken: cancellationToken))
        {
            var wardId = ReadString(assignment, "WardId");
            var tenantId = ReadString(assignment, "TenantId");
            if (IsActive(assignment)
                && tenantId.Length > 0
                && wardId.Length > 0
                && (userTenant is null || string.Equals(tenantId, userTenant, StringComparison.Ordinal)))
            {
                wards.Add(wardId);
            }
        }

        return new DirectoryUser
        {
            DirectoryId = directoryId,
            ObjectId = objectId,
            TenantId = userTenant,
            Role = ParseRole(ReadString(response.Value, "Role")),
            IsActive = IsActive(response.Value),
            WardIds = wards
        };
    }

    public async Task<IReadOnlyList<WardOption>> GetVisibleWardsAsync(AccessDecision access, CancellationToken cancellationToken)
    {
        if (!access.Allowed || access.Role is null)
        {
            return [];
        }

        if (access.Role == AppRole.SuperAdmin)
        {
            return await AllActiveWardsAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(access.TenantId))
        {
            return [];
        }

        var customer = await GetCustomerAsync(access.TenantId, cancellationToken);
        if (customer is null || !customer.IsActive)
        {
            return [];
        }

        var user = await GetUserAsync(access.DirectoryId, access.ObjectId, cancellationToken);
        var allowed = user?.WardIds ?? new HashSet<string>(StringComparer.Ordinal);
        var wards = new List<WardOption>();
        await foreach (var ward in _wards.QueryAsync<TableEntity>(
            $"PartitionKey eq '{ODataLiteral.Escape(access.TenantId)}'",
            cancellationToken: cancellationToken))
        {
            if (!IsActive(ward) || !allowed.Contains(ward.RowKey))
            {
                continue;
            }

            wards.Add(ToWard(access.TenantId, ward));
        }

        return wards;
    }

    public async Task SaveCustomerAsync(string tenantId, string name, bool active, CancellationToken cancellationToken)
    {
        var entity = new TableEntity("customer", tenantId)
        {
            ["CustomerName"] = name.Trim(),
            ["Status"] = active ? "Active" : "Inactive",
            ["CreatedAtUtc"] = DateTimeOffset.UtcNow,
            ["SubscriptionNote"] = ""
        };
        await _customers.UpsertEntityAsync(entity, TableUpdateMode.Merge, cancellationToken);
    }

    public async Task SaveWardAsync(string tenantId, string wardId, string displayName, int? expectedVoters, bool active, CancellationToken cancellationToken)
    {
        var entity = new TableEntity(tenantId, wardId)
        {
            ["DisplayName"] = displayName.Trim(),
            ["Status"] = active ? "Active" : "Inactive",
            ["ExpectedVoters"] = expectedVoters
        };
        await _wards.UpsertEntityAsync(entity, TableUpdateMode.Merge, cancellationToken);
    }

    public async Task SaveUserAsync(DirectoryUser user, CancellationToken cancellationToken)
    {
        var entity = new TableEntity(user.DirectoryId, user.ObjectId)
        {
            ["TenantId"] = user.TenantId ?? "",
            ["Role"] = user.Role.ToString(),
            ["Status"] = user.IsActive ? "Active" : "Inactive",
            ["CreatedAtUtc"] = DateTimeOffset.UtcNow
        };
        await _users.UpsertEntityAsync(entity, TableUpdateMode.Merge, cancellationToken);
    }

    public async Task SaveAssignmentAsync(string directoryId, string objectId, string tenantId, string wardId, bool active, CancellationToken cancellationToken)
    {
        var entity = new TableEntity(AssignmentPartition(directoryId, objectId), $"{tenantId}|{wardId}")
        {
            ["TenantId"] = tenantId,
            ["WardId"] = wardId,
            ["Status"] = active ? "Active" : "Inactive"
        };
        await _assignments.UpsertEntityAsync(entity, TableUpdateMode.Merge, cancellationToken);
    }

    private async Task<IReadOnlyList<WardOption>> AllActiveWardsAsync(CancellationToken cancellationToken)
    {
        var wards = new List<WardOption>();
        await foreach (var customer in _customers.QueryAsync<TableEntity>("PartitionKey eq 'customer'", cancellationToken: cancellationToken))
        {
            if (!IsActive(customer) || !TenantPartitions.IsCustomerTenantId(customer.RowKey))
            {
                continue;
            }

            await foreach (var ward in _wards.QueryAsync<TableEntity>(
                $"PartitionKey eq '{ODataLiteral.Escape(customer.RowKey)}'",
                cancellationToken: cancellationToken))
            {
                if (IsActive(ward))
                {
                    wards.Add(ToWard(customer.RowKey, ward));
                }
            }
        }

        return wards;
    }

    private static WardOption ToWard(string tenantId, TableEntity ward)
    {
        int? expected = null;
        if (ward.TryGetValue("ExpectedVoters", out var raw) && raw is int count)
        {
            expected = count;
        }
        else if (raw is long longCount)
        {
            expected = checked((int)longCount);
        }

        var name = ReadString(ward, "DisplayName");
        return new WardOption
        {
            TenantId = tenantId,
            WardId = ward.RowKey,
            DisplayName = name.Length == 0 ? $"Ward {ward.RowKey}" : name,
            ExpectedVoters = expected,
            IsConfigured = true
        };
    }

    private static string AssignmentPartition(string directoryId, string objectId) => $"{directoryId}|{objectId}";

    private static bool IsActive(TableEntity entity) =>
        !string.Equals(ReadString(entity, "Status"), "Inactive", StringComparison.OrdinalIgnoreCase);

    private static string ReadString(TableEntity entity, string name) =>
        entity.TryGetValue(name, out var value) ? value?.ToString() ?? "" : "";

    private static string? BlankToNull(string value) => value.Length == 0 ? null : value;

    private static AppRole ParseRole(string value) =>
        Enum.TryParse<AppRole>(value, ignoreCase: false, out var role) ? role : AppRole.WardOperator;
}
