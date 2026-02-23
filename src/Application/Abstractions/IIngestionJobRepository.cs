using Domain;

namespace Application.Abstractions;

public enum SaveSubmissionOutcome
{
    Saved,
    Duplicate
}

public sealed record SaveSubmissionResult(SaveSubmissionOutcome Outcome, Guid? ExistingJobId = null);

public interface IIngestionJobRepository
{
    Task<Guid?> FindJobIdByTenantAndIdempotencyAsync(string tenantId, string idempotencyKey, CancellationToken ct);
    void Add(IngestionJob job);
    Task<SaveSubmissionResult> SaveSubmissionAsync(string tenantId, string? idempotencyKey, CancellationToken ct);
    Task<IngestionJob?> GetByIdAsync(Guid jobId, CancellationToken ct);
    Task<bool> ExistsAsync(Guid jobId, CancellationToken ct);
}
