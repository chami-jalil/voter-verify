using VoterVerification.Models;

namespace VoterVerification.Services;

public interface IVoterDataService
{
    Task<IReadOnlyList<WardOption>> GetAssignedWardsAsync(CancellationToken cancellationToken);

    Task<DashboardStats> GetDashboardStatsAsync(string wardId, CancellationToken cancellationToken);

    Task<VoterSearchOutcome> SearchAsync(string wardId, string query, CancellationToken cancellationToken);

    Task<VoterDetails?> GetVoterAsync(string wardId, string serialNumber, CancellationToken cancellationToken);

    Task<VoterVerificationRecord> SaveVerificationAsync(VerificationDraft draft, CancellationToken cancellationToken);
}
