using VoterVerification.Data;

namespace VoterDataImporter.Import;

public sealed class VoterBatchSubmitException : Exception
{
    public VoterBatchSubmitException(int status, int? failedIndex)
        : base($"Table batch failed with status {status}.")
    {
        Status = status;
        FailedIndex = failedIndex;
    }

    public int Status { get; }

    public int? FailedIndex { get; }
}

public sealed class RowWrite
{
    public required VoterTableEntity Entity { get; init; }

    public required string Reason { get; init; }
}

public sealed class BatchWriteResult
{
    public List<VoterTableEntity> Inserted { get; } = [];

    public List<RowWrite> Skipped { get; } = [];

    public List<RowWrite> Failed { get; } = [];
}

public static class VoterBatchInserter
{
    public const int MaxBatchSize = 100;

    public static async Task<BatchWriteResult> InsertAsync(
        IReadOnlyList<VoterTableEntity> entities,
        Func<IReadOnlyList<VoterTableEntity>, CancellationToken, Task> submit,
        CancellationToken cancellationToken)
    {
        var result = new BatchWriteResult();
        foreach (var group in entities.GroupBy(entity => entity.PartitionKey, StringComparer.Ordinal))
        {
            foreach (var chunk in Chunk(group, MaxBatchSize))
            {
                await InsertChunkAsync(chunk, submit, result, cancellationToken);
            }
        }

        return result;
    }

    private static async Task InsertChunkAsync(
        IReadOnlyList<VoterTableEntity> chunk,
        Func<IReadOnlyList<VoterTableEntity>, CancellationToken, Task> submit,
        BatchWriteResult result,
        CancellationToken cancellationToken)
    {
        if (chunk.Count == 0)
        {
            return;
        }

        if (chunk.Select(entity => entity.PartitionKey).Distinct(StringComparer.Ordinal).Count() != 1)
        {
            throw new InvalidOperationException("A table batch can contain only one PartitionKey.");
        }

        try
        {
            await submit(chunk, cancellationToken);
            result.Inserted.AddRange(chunk);
        }
        catch (VoterBatchSubmitException ex) when (chunk.Count == 1)
        {
            if (ex.Status == 409)
            {
                result.Skipped.Add(new RowWrite
                {
                    Entity = chunk[0],
                    Reason = "The voter row already exists and was left unchanged."
                });
            }
            else
            {
                result.Failed.Add(new RowWrite
                {
                    Entity = chunk[0],
                    Reason = $"Table request failed with status {ex.Status}."
                });
            }
        }
        catch (VoterBatchSubmitException ex) when (ex.FailedIndex is int index && index >= 0 && index < chunk.Count && chunk.Count > 1)
        {
            var rest = chunk.Where((_, itemIndex) => itemIndex != index).ToList();
            await InsertChunkAsync([chunk[index]], submit, result, cancellationToken);
            if (rest.Count > 0)
            {
                await InsertChunkAsync(rest, submit, result, cancellationToken);
            }
        }
        catch (VoterBatchSubmitException) when (chunk.Count > 1)
        {
            var midpoint = chunk.Count / 2;
            await InsertChunkAsync(chunk.Take(midpoint).ToList(), submit, result, cancellationToken);
            await InsertChunkAsync(chunk.Skip(midpoint).ToList(), submit, result, cancellationToken);
        }
        catch (Exception ex) when (ex is not VoterBatchSubmitException && chunk.Count == 1)
        {
            result.Failed.Add(new RowWrite
            {
                Entity = chunk[0],
                Reason = ex.GetType().Name
            });
        }
        catch (Exception ex) when (ex is not VoterBatchSubmitException && chunk.Count > 1)
        {
            var midpoint = chunk.Count / 2;
            await InsertChunkAsync(chunk.Take(midpoint).ToList(), submit, result, cancellationToken);
            await InsertChunkAsync(chunk.Skip(midpoint).ToList(), submit, result, cancellationToken);
        }
    }

    private static List<List<T>> Chunk<T>(IEnumerable<T> items, int size)
    {
        var chunks = new List<List<T>>();
        var current = new List<T>(size);
        foreach (var item in items)
        {
            current.Add(item);
            if (current.Count == size)
            {
                chunks.Add(current);
                current = new List<T>(size);
            }
        }

        if (current.Count > 0)
        {
            chunks.Add(current);
        }

        return chunks;
    }
}
