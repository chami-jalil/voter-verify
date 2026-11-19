using Azure;
using Azure.Core;
using Azure.Data.Tables;
using VoterVerification.Configuration;
using VoterVerification.Data;

namespace VoterDataImporter.Import;

public sealed class AzureVoterTableStore : IVoterTableStore
{
    private readonly TableClient _voters;

    public AzureVoterTableStore(string accountName, TokenCredential credential)
    {
        if (!IsAccountName(accountName))
        {
            throw new ArgumentException("Pass the storage account name only. Do not pass a key, SAS, or connection string.");
        }

        var options = new TableClientOptions
        {
            Diagnostics =
            {
                IsLoggingContentEnabled = false,
                IsTelemetryEnabled = false
            }
        };
        options.Retry.MaxRetries = 2;
        _voters = new TableClient(
            new Uri($"https://{accountName}.table.core.windows.net"),
            AppConfiguration.VotersTableName,
            credential,
            options);
    }

    public async Task<IReadOnlyList<VoterTableEntity>> ReadPartitionAsync(string wardId, CancellationToken cancellationToken)
    {
        var filter = $"PartitionKey eq '{wardId.Replace("'", "''", StringComparison.Ordinal)}'";
        var rows = new List<VoterTableEntity>();
        await foreach (var entity in _voters.QueryAsync<VoterTableEntity>(filter, cancellationToken: cancellationToken))
        {
            rows.Add(entity);
        }

        return rows;
    }

    public Task<BatchWriteResult> InsertAsync(IReadOnlyList<VoterTableEntity> entities, CancellationToken cancellationToken) =>
        VoterBatchInserter.InsertAsync(entities, SubmitAddsAsync, cancellationToken);

    private async Task SubmitAddsAsync(IReadOnlyList<VoterTableEntity> chunk, CancellationToken cancellationToken)
    {
        var actions = chunk
            .Select(entity => new TableTransactionAction(TableTransactionActionType.Add, entity))
            .ToList();
        try
        {
            await _voters.SubmitTransactionAsync(actions, cancellationToken);
        }
        catch (TableTransactionFailedException ex)
        {
            throw new VoterBatchSubmitException(ex.Status, ex.FailedTransactionActionIndex);
        }
    }

    public static bool IsAccountName(string? accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName) || accountName.Length is < 3 or > 24)
        {
            return false;
        }

        return accountName.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9');
    }
}
