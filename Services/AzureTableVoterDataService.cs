using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using VoterVerification.Authentication;
using VoterVerification.Configuration;
using VoterVerification.Data;
using VoterVerification.Helpers;
using VoterVerification.Models;

namespace VoterVerification.Services;

public sealed class AzureTableVoterDataService : IVoterDataService
{
    private const int PageSize = 500;
    private const int MaxPages = 20;
    private const int MaxHits = 50;

    private readonly MsalStorageCredential _credential;
    private readonly IEntraAuthService _auth;
    private readonly ILogger<AzureTableVoterDataService> _logger;
    private readonly TableClient _voters;
    private readonly TableClient _verifications;

    public AzureTableVoterDataService(IEntraAuthService auth, ILogger<AzureTableVoterDataService> logger)
    {
        _auth = auth;
        _logger = logger;
        _credential = new MsalStorageCredential(auth);
        _voters = CreateClient(AppConfiguration.VotersTableName);
        _verifications = CreateClient(AppConfiguration.VerificationsTableName);
    }

    public async Task<DashboardStats> GetDashboardStatsAsync(string wardId, CancellationToken cancellationToken)
    {
        EnsureWard(wardId);
        try
        {
            var votersTask = CountAsync(_voters, VoterQueryBuilder.Partition(wardId), cancellationToken);
            var verifiedTask = CountAsync(_verifications, VoterQueryBuilder.VerifiedInWard(wardId), cancellationToken);
            await Task.WhenAll(votersTask, verifiedTask);
            return new DashboardStats
            {
                VotersOnFile = votersTask.Result,
                VerifiedCount = verifiedTask.Result
            };
        }
        catch (Exception ex)
        {
            throw Translate(ex);
        }
    }

    public async Task<VoterSearchOutcome> SearchAsync(string wardId, string query, CancellationToken cancellationToken)
    {
        EnsureWard(wardId);
        var mode = VoterLookup.Classify(query);
        if (mode == VoterLookupMode.Invalid)
        {
            throw new AppException(AppErrorKind.Validation, VoterLookup.ValidationMessage(query));
        }

        try
        {
            var hits = new Dictionary<string, VoterSearchHit>(StringComparer.Ordinal);
            var truncated = false;
            var scanLimitReached = false;
            var trimmed = query.Trim();

            if (mode == VoterLookupMode.Epic)
            {
                var epic = VoterLookup.NormalizeEpic(trimmed);
                await CollectQueryAsync(
                    _voters,
                    VoterQueryBuilder.Epic(wardId, epic),
                    hits,
                    _ => true,
                    cancellationToken);
            }
            else if (mode == VoterLookupMode.KeyAndHouse)
            {
                if (TableKeyRules.IsValid(trimmed))
                {
                    var exact = await ReadVoterAsync(wardId, trimmed, cancellationToken);
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
                        _voters,
                        VoterQueryBuilder.House(wardId, trimmed, normalizedHouse),
                        hits,
                        _ => true,
                        cancellationToken);
                }
            }
            else
            {
                var pages = 0;
                await foreach (var page in _voters.QueryAsync<VoterTableEntity>(
                    VoterQueryBuilder.Partition(wardId),
                    maxPerPage: PageSize,
                    cancellationToken: cancellationToken).AsPages())
                {
                    pages++;
                    foreach (var entity in page.Values)
                    {
                        if (!VoterTextMatch.MatchesName(
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

                    if (truncated)
                    {
                        break;
                    }

                    if (pages >= MaxPages)
                    {
                        scanLimitReached = true;
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
        catch (Exception ex)
        {
            throw Translate(ex);
        }
    }

    public async Task<VoterDetails?> GetVoterAsync(string wardId, string serialNumber, CancellationToken cancellationToken)
    {
        EnsureWard(wardId);
        if (!TableKeyRules.IsValid(serialNumber))
        {
            throw new AppException(AppErrorKind.Validation, "That serial number cannot be used as a table key.");
        }

        try
        {
            var voterTask = ReadVoterAsync(wardId, serialNumber, cancellationToken);
            var verificationTask = ReadVerificationAsync(wardId, serialNumber, cancellationToken);
            await Task.WhenAll(voterTask, verificationTask);
            if (voterTask.Result is null)
            {
                return null;
            }

            return new VoterDetails
            {
                Voter = TableEntityMapper.ToRecord(voterTask.Result),
                Verification = verificationTask.Result is null ? null : TableEntityMapper.ToRecord(verificationTask.Result)
            };
        }
        catch (Exception ex)
        {
            throw Translate(ex);
        }
    }

    public async Task<VoterVerificationRecord> SaveVerificationAsync(VerificationDraft draft, CancellationToken cancellationToken)
    {
        EnsureWard(draft.WardId);
        if (!TableKeyRules.IsValid(draft.SerialNumber))
        {
            throw new AppException(AppErrorKind.Validation, "That serial number cannot be used as a table key.");
        }

        var user = _auth.CurrentUser ?? throw new AppException(AppErrorKind.SignInRequired, "Sign in again to continue.");
        VerificationWritePlan plan;
        try
        {
            plan = VerificationUpdatePolicy.Create(draft, user, DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException)
        {
            throw new AppException(AppErrorKind.Conflict, "Reload this voter before saving. The verification tag is missing.");
        }

        var entity = TableEntityMapper.ToEntity(draft.WardId, draft.SerialNumber, plan);
        try
        {
            if (plan.Insert)
            {
                await _verifications.AddEntityAsync(entity, cancellationToken);
            }
            else
            {
                // Replace plus If-Match writes only the verification table and clears
                // properties that are now null, such as VerifiedAtUtc when unverified.
                entity.ETag = new ETag(plan.IfMatchETag!);
                await _verifications.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, cancellationToken);
            }

            var saved = await ReadVerificationAsync(draft.WardId, draft.SerialNumber, cancellationToken);
            if (saved is null)
            {
                throw new AppException(AppErrorKind.Unexpected, "The verification was saved but could not be read back.");
            }

            return TableEntityMapper.ToRecord(saved);
        }
        catch (Exception ex)
        {
            throw Translate(ex);
        }
    }

    private async Task<VoterTableEntity?> ReadVoterAsync(string wardId, string serialNumber, CancellationToken cancellationToken)
    {
        var response = await _voters.GetEntityIfExistsAsync<VoterTableEntity>(wardId, serialNumber, cancellationToken: cancellationToken);
        return response.HasValue ? response.Value : null;
    }

    private async Task<VoterVerificationTableEntity?> ReadVerificationAsync(string wardId, string serialNumber, CancellationToken cancellationToken)
    {
        var response = await _verifications.GetEntityIfExistsAsync<VoterVerificationTableEntity>(wardId, serialNumber, cancellationToken: cancellationToken);
        return response.HasValue ? response.Value : null;
    }

    private static async Task<bool> CollectQueryAsync(
        TableClient client,
        string filter,
        Dictionary<string, VoterSearchHit> hits,
        Func<VoterTableEntity, bool> predicate,
        CancellationToken cancellationToken)
    {
        await foreach (var page in client.QueryAsync<VoterTableEntity>(filter, maxPerPage: PageSize, cancellationToken: cancellationToken).AsPages())
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

    private static async Task<int> CountAsync(TableClient client, string filter, CancellationToken cancellationToken)
    {
        var count = 0;
        await foreach (var page in client.QueryAsync<TableEntity>(
            filter,
            maxPerPage: 1000,
            select: ["RowKey"],
            cancellationToken: cancellationToken).AsPages())
        {
            count += page.Values.Count;
        }

        return count;
    }

    private Exception Translate(Exception exception)
    {
        if (exception is AppException)
        {
            return exception;
        }

        if (exception is RequestFailedException requestFailed)
        {
            _logger.LogWarning(
                "Table request failed. Status {Status} ErrorCode {ErrorCode}.",
                requestFailed.Status,
                requestFailed.ErrorCode);
            return ErrorMapper.FromAzure(requestFailed);
        }

        if (exception is AggregateException aggregate)
        {
            var inner = aggregate.Flatten().InnerExceptions;
            var failed = inner.OfType<RequestFailedException>().FirstOrDefault();
            if (failed is not null)
            {
                return Translate(failed);
            }

            if (inner.Count > 0)
            {
                return Translate(inner[0]);
            }
        }

        return exception;
    }

    private static void EnsureWard(string wardId)
    {
        var ward = WardCatalog.Find(wardId);
        if (ward is null || !ward.IsConfigured || !TableKeyRules.IsValid(wardId))
        {
            throw new AppException(AppErrorKind.Validation, "This ward is not configured on this device.");
        }
    }

    private TableClient CreateClient(string tableName)
    {
        var options = new TableClientOptions
        {
            Diagnostics =
            {
                IsLoggingContentEnabled = false,
                IsTelemetryEnabled = false
            }
        };
        options.Retry.Mode = Azure.Core.RetryMode.Exponential;
        options.Retry.MaxRetries = 2;
        options.Retry.Delay = TimeSpan.FromMilliseconds(800);
        options.Retry.NetworkTimeout = TimeSpan.FromSeconds(60);

        if (!AppConfiguration.IsConfigured)
        {
            return new TableClient(new Uri("https://unconfigured.table.core.windows.net"), tableName, _credential, options);
        }

        return new TableClient(new Uri(AppConfiguration.TableServiceUri), tableName, _credential, options);
    }
}
