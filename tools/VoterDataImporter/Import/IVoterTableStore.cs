using VoterVerification.Data;

namespace VoterDataImporter.Import;

public interface IVoterTableStore
{
    Task<IReadOnlyList<VoterTableEntity>> ReadPartitionAsync(string wardId, CancellationToken cancellationToken);

    Task<BatchWriteResult> InsertAsync(IReadOnlyList<VoterTableEntity> entities, CancellationToken cancellationToken);
}
