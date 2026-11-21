using Azure;
using Azure.Data.Tables;
using VoterVerification.Data;
using VoterVerification.Models;
using VoterVerification.Services;

namespace VoterVerification.Helpers;

public static class VoterPartitionSearch
{
    public const int PageSize = 500;
    public const int MaxPages = 20;
    public const int MaxHits = 50;

    public static async Task<VoterSearchOutcome> SearchAsync(
        string partitionKey,
        string query,
        Func<string, CancellationToken, Task<VoterTableEntity?>> readExact,
        Func<string, int, CancellationToken, AsyncPageable<VoterTableEntity>> runQuery,
        CancellationToken cancellationToken,
        Func<VoterTableEntity, bool>? include = null)
    {
        var mode = VoterLookup.Classify(query);
        if (mode == VoterLookupMode.Invalid)
        {
            throw new AppException(AppErrorKind.Validation, VoterLookup.ValidationMessage(query));
        }

        var hits = new Dictionary<string, VoterSearchHit>(StringComparer.Ordinal);
        var truncated = false;
        var scanLimitReached = false;
        var trimmed = query.Trim();

        if (mode == VoterLookupMode.Epic)
        {
            var epic = VoterLookup.NormalizeEpic(trimmed);
            await CollectQueryAsync(
                runQuery(VoterQueryBuilder.Epic(partitionKey, epic), PageSize, cancellationToken),
                hits,
                entity => include is null || include(entity),
                cancellationToken);
        }
        else if (mode == VoterLookupMode.AssemblyRoll)
        {
            if (TableKeyRules.IsValid(trimmed))
            {
                var exact = await readExact(trimmed, cancellationToken);
                if (exact is not null)
                {
                    var hit = TableEntityMapper.ToHit(exact);
                    hits[hit.SerialNumber] = hit;
                }
            }

            var reference = VoterLookup.NormalizeAssemblyRoll(trimmed);
            truncated = await CollectQueryAsync(
                runQuery(VoterQueryBuilder.AssemblyRoll(partitionKey, reference), PageSize, cancellationToken),
                hits,
                entity => (include is null || include(entity))
                    && VoterTextMatch.MatchesAssemblyRoll(entity.AssemblyRollReference, trimmed),
                cancellationToken);
        }
        else if (mode == VoterLookupMode.KeyAndHouse)
        {
            if (TableKeyRules.IsValid(trimmed))
            {
                var exact = await readExact(trimmed, cancellationToken);
                if (exact is not null)
                {
                    var hit = TableEntityMapper.ToHit(exact);
                    hits[hit.SerialNumber] = hit;
                }
            }

            var normalizedHouse = VoterLookup.NormalizeHouse(trimmed);
            if (normalizedHouse.Length > 0)
            {
                truncated = await CollectQueryAsync(
                    runQuery(VoterQueryBuilder.House(partitionKey, trimmed, normalizedHouse), PageSize, cancellationToken),
                    hits,
                    entity => (include is null || include(entity))
                        && VoterTextMatch.MatchesHouse(entity.HouseNumber, entity.NormalizedHouseNumber, trimmed),
                    cancellationToken);
            }
        }
        else
        {
            var pages = 0;
            await foreach (var page in runQuery(VoterQueryBuilder.Partition(partitionKey), PageSize, cancellationToken).AsPages().WithCancellation(cancellationToken))
            {
                pages++;
                foreach (var entity in page.Values)
                {
                    if ((include is not null && !include(entity))
                        || !VoterTextMatch.MatchesName(
                        entity.NameTelugu,
                        entity.NameEnglish,
                        entity.RelativeNameTelugu,
                        entity.RelativeNameEnglish,
                        trimmed))
                    {
                        continue;
                    }

                    var hit = TableEntityMapper.ToHit(entity);
                    hits[hit.SerialNumber] = hit;
                    if (hits.Count >= MaxHits)
                    {
                        truncated = true;
                        break;
                    }
                }

                if (truncated || pages >= MaxPages)
                {
                    scanLimitReached = !truncated && pages >= MaxPages;
                    break;
                }
            }
        }

        var ordered = hits.Values
            .OrderBy(hit => hit.SerialNumber, Comparer<string>.Create(VoterTextMatch.CompareSerial))
            .Take(MaxHits)
            .ToArray();
        if (hits.Count > MaxHits)
        {
            truncated = true;
        }

        return new VoterSearchOutcome
        {
            Hits = ordered,
            Truncated = truncated,
            ScanLimitReached = scanLimitReached
        };
    }

    private static async Task<bool> CollectQueryAsync(
        AsyncPageable<VoterTableEntity> pages,
        Dictionary<string, VoterSearchHit> hits,
        Func<VoterTableEntity, bool> predicate,
        CancellationToken cancellationToken)
    {
        await foreach (var page in pages.AsPages().WithCancellation(cancellationToken))
        {
            foreach (var entity in page.Values)
            {
                if (!predicate(entity))
                {
                    continue;
                }

                var hit = TableEntityMapper.ToHit(entity);
                hits[hit.SerialNumber] = hit;
                if (hits.Count >= MaxHits)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
