using Azure;
using Azure.Data.Tables;
using VoterVerification.Configuration;
using VoterVerification.Data;
using VoterVerification.Helpers;
using VoterVerification.Models;
using VoterVerification.Services;

namespace VoterVerification.Functions.Data;

public sealed class VoterGateway
{
    private readonly TableClient _voters;
    private readonly TableClient _verifications;
    private readonly string? _initialTenantId;

    public VoterGateway(TableClient voters, TableClient verifications, string? initialTenantId)
    {
        _voters = voters;
        _verifications = verifications;
        _initialTenantId = string.IsNullOrWhiteSpace(initialTenantId) ? null : initialTenantId.Trim();
    }

    public async Task<DashboardStats> GetStatsAsync(string tenantId, string wardId, CancellationToken cancellationToken)
    {
        var voterKeys = new HashSet<string>(StringComparer.Ordinal);
        var verifiedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var partition in TenantPartitions.ReadKeys(tenantId, wardId, _initialTenantId))
        {
            await CollectKeysAsync(_voters, partition, tenantId, wardId, voterKeys, cancellationToken);
            await CollectVerifiedKeysAsync(partition, tenantId, wardId, verifiedKeys, cancellationToken);
        }

        return new DashboardStats
        {
            VotersOnFile = voterKeys.Count,
            VerifiedCount = verifiedKeys.Count,
            ExpectedVoters = await ExpectedVotersAsync(tenantId, wardId, cancellationToken)
        };
    }

    public async Task<VoterSearchOutcome> SearchAsync(string tenantId, string wardId, string query, CancellationToken cancellationToken)
    {
        var hits = new Dictionary<string, VoterSearchHit>(StringComparer.Ordinal);
        var truncated = false;
        var scanLimitReached = false;
        foreach (var partition in TenantPartitions.ReadKeys(tenantId, wardId, _initialTenantId))
        {
            var outcome = await VoterPartitionSearch.SearchAsync(
                partition,
                query,
                (serial, token) => ReadVisibleVoterAsync(partition, serial, tenantId, wardId, token),
                (filter, pageSize, token) => _voters.QueryAsync<VoterTableEntity>(filter, maxPerPage: pageSize, cancellationToken: token),
                cancellationToken,
                entity => TenantPartitions.CanRead(entity.PartitionKey, entity.TenantId, entity.WardId, entity.RowKey, tenantId, wardId, _initialTenantId));
            foreach (var hit in outcome.Hits)
            {
                hits.TryAdd(hit.SerialNumber, hit);
            }

            truncated |= outcome.Truncated;
            scanLimitReached |= outcome.ScanLimitReached;
        }

        return new VoterSearchOutcome
        {
            Hits = hits.Values
                .OrderBy(hit => hit.SerialNumber, Comparer<string>.Create(VoterTextMatch.CompareSerial))
                .Take(VoterPartitionSearch.MaxHits)
                .ToArray(),
            Truncated = truncated || hits.Count > VoterPartitionSearch.MaxHits,
            ScanLimitReached = scanLimitReached
        };
    }

    public async Task<VoterDetails?> GetVoterAsync(string tenantId, string wardId, string serialNumber, CancellationToken cancellationToken)
    {
        var voter = await FindVoterAsync(tenantId, wardId, serialNumber, cancellationToken);
        if (voter is null)
        {
            return null;
        }

        var verification = await ReadVerificationAsync(voter.PartitionKey, serialNumber, tenantId, wardId, cancellationToken);
        return new VoterDetails
        {
            Voter = TableEntityMapper.ToRecord(voter),
            Verification = verification is null ? null : TableEntityMapper.ToRecord(verification)
        };
    }

    public async Task<IReadOnlyList<VoterVerificationRecord>> GetHistoryAsync(string tenantId, string wardId, string serialNumber, CancellationToken cancellationToken)
    {
        var details = await GetVoterAsync(tenantId, wardId, serialNumber, cancellationToken);
        if (details?.Verification is null)
        {
            return details is null ? [] : [];
        }

        return [details.Verification];
    }

    public async Task<VoterVerificationRecord> SaveAsync(
        string tenantId,
        string wardId,
        VerificationDraft draft,
        SignedInUser user,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(draft.WardId, wardId, StringComparison.Ordinal))
        {
            throw new AppException(AppErrorKind.NotAuthorized, "You cannot use this ward.");
        }

        if (!PhoneNumberRules.TryNormalize(draft.PhoneNumber, out var phone, out var phoneError))
        {
            throw new AppException(AppErrorKind.Validation, phoneError ?? "Enter a 10-digit phone number.");
        }

        if (draft.IsVerified && phone.Length == 0)
        {
            throw new AppException(AppErrorKind.Validation, "A phone number is required when marking the voter verified.");
        }

        var voter = await FindVoterAsync(tenantId, wardId, draft.SerialNumber, cancellationToken)
            ?? throw new AppException(AppErrorKind.NotFound, "That record was not found.");
        var existing = await ReadVerificationAsync(voter.PartitionKey, draft.SerialNumber, tenantId, wardId, cancellationToken);
        var normalized = new VerificationDraft
        {
            WardId = wardId,
            SerialNumber = draft.SerialNumber,
            PhoneNumber = phone,
            IsVerified = draft.IsVerified,
            HasExistingRow = existing is not null,
            ETag = existing?.ETag.ToString(),
            WasVerified = existing?.IsVerified == true,
            PreviousVerifiedAtUtc = existing?.VerifiedAtUtc
        };
        VerificationWritePlan plan;
        try
        {
            plan = VerificationUpdatePolicy.Create(normalized, user, DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException)
        {
            throw new AppException(AppErrorKind.Conflict, "Reload this voter before saving. The verification tag is missing.");
        }

        var entity = TableEntityMapper.ToEntity(voter.PartitionKey, draft.SerialNumber, plan);
        entity.WardId = wardId;
        entity.TenantId = tenantId;
        if (plan.Insert)
        {
            await _verifications.AddEntityAsync(entity, cancellationToken);
        }
        else
        {
            entity.ETag = new ETag(plan.IfMatchETag!);
            await _verifications.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, cancellationToken);
        }

        var saved = await ReadVerificationAsync(voter.PartitionKey, draft.SerialNumber, tenantId, wardId, cancellationToken)
            ?? throw new AppException(AppErrorKind.Unexpected, "The verification was saved but could not be read back.");
        return TableEntityMapper.ToRecord(saved);
    }

    private async Task<VoterTableEntity?> FindVoterAsync(string tenantId, string wardId, string serialNumber, CancellationToken cancellationToken)
    {
        if (!TableKeyRules.IsValid(serialNumber))
        {
            throw new AppException(AppErrorKind.Validation, "That serial number cannot be used as a table key.");
        }

        foreach (var partition in TenantPartitions.ReadKeys(tenantId, wardId, _initialTenantId))
        {
            var voter = await ReadVisibleVoterAsync(partition, serialNumber, tenantId, wardId, cancellationToken);
            if (voter is not null)
            {
                return voter;
            }
        }

        return null;
    }

    private async Task<VoterTableEntity?> ReadVisibleVoterAsync(
        string partition,
        string serialNumber,
        string tenantId,
        string wardId,
        CancellationToken cancellationToken)
    {
        var response = await _voters.GetEntityIfExistsAsync<VoterTableEntity>(partition, serialNumber, cancellationToken: cancellationToken);
        if (!response.HasValue || response.Value is null)
        {
            return null;
        }

        var entity = response.Value;
        return TenantPartitions.CanRead(entity.PartitionKey, entity.TenantId, entity.WardId, entity.RowKey, tenantId, wardId, _initialTenantId)
            ? entity
            : null;
    }

    private async Task<VoterVerificationTableEntity?> ReadVerificationAsync(
        string partition,
        string serialNumber,
        string tenantId,
        string wardId,
        CancellationToken cancellationToken)
    {
        var response = await _verifications.GetEntityIfExistsAsync<VoterVerificationTableEntity>(partition, serialNumber, cancellationToken: cancellationToken);
        if (!response.HasValue || response.Value is null)
        {
            return null;
        }

        var entity = response.Value;
        if (!TenantPartitions.CanRead(entity.PartitionKey, entity.TenantId, entity.WardId, entity.RowKey, tenantId, wardId, _initialTenantId))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(entity.WardId))
        {
            entity.WardId = wardId;
        }

        return entity;
    }

    private async Task CollectKeysAsync(
        TableClient client,
        string partition,
        string tenantId,
        string wardId,
        HashSet<string> keys,
        CancellationToken cancellationToken)
    {
        var filter = VoterQueryBuilder.Partition(partition);
        await foreach (var entity in client.QueryAsync<VoterTableEntity>(filter, select: ["PartitionKey", "RowKey", "TenantId", "WardId", "WardVoterSerialNumber"], cancellationToken: cancellationToken))
        {
            if (TenantPartitions.CanRead(entity.PartitionKey, entity.TenantId, entity.WardId, entity.RowKey, tenantId, wardId, _initialTenantId))
            {
                keys.Add(string.IsNullOrWhiteSpace(entity.WardVoterSerialNumber) ? entity.RowKey : entity.WardVoterSerialNumber);
            }
        }
    }

    private async Task CollectVerifiedKeysAsync(
        string partition,
        string tenantId,
        string wardId,
        HashSet<string> keys,
        CancellationToken cancellationToken)
    {
        var filter = $"{VoterQueryBuilder.Partition(partition)} and IsVerified eq true";
        await foreach (var entity in _verifications.QueryAsync<VoterVerificationTableEntity>(
            filter,
            select: ["PartitionKey", "RowKey", "TenantId", "WardId", "IsVerified"],
            cancellationToken: cancellationToken))
        {
            if (TenantPartitions.CanRead(entity.PartitionKey, entity.TenantId, entity.WardId, entity.RowKey, tenantId, wardId, _initialTenantId))
            {
                keys.Add(entity.RowKey);
            }
        }
    }

    private Task<int?> ExpectedVotersAsync(string tenantId, string wardId, CancellationToken cancellationToken)
    {
        if (string.Equals(wardId, AppConfiguration.Ward10Id, StringComparison.Ordinal)
            && TenantPartitions.IsInitialTenant(tenantId, _initialTenantId))
        {
            return Task.FromResult<int?>(AppConfiguration.Ward10ExpectedVoters);
        }

        return Task.FromResult<int?>(null);
    }
}
