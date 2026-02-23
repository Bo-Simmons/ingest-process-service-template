using Application.Abstractions;
using Domain;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class IngestionJobRepository(IngestionDbContext db) : IIngestionJobRepository
{
    public async Task<Guid?> FindJobIdByTenantAndIdempotencyAsync(string tenantId, string idempotencyKey, CancellationToken ct)
    {
        var existing = await db.IngestionJobs
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IdempotencyKey == idempotencyKey)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(ct);

        return existing == Guid.Empty ? null : existing;
    }

    public void Add(IngestionJob job) => db.IngestionJobs.Add(job);

    public async Task<SaveSubmissionResult> SaveSubmissionAsync(string tenantId, string? idempotencyKey, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return new SaveSubmissionResult(SaveSubmissionOutcome.Saved);
        }
        catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existingJobId = await FindJobIdByTenantAndIdempotencyAsync(tenantId, idempotencyKey!, ct);
            if (existingJobId.HasValue)
            {
                return new SaveSubmissionResult(SaveSubmissionOutcome.Duplicate, existingJobId);
            }

            throw;
        }
    }

    public Task<IngestionJob?> GetByIdAsync(Guid jobId, CancellationToken ct) =>
        db.IngestionJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == jobId, ct);

    public Task<bool> ExistsAsync(Guid jobId, CancellationToken ct) =>
        db.IngestionJobs.AsNoTracking().AnyAsync(x => x.Id == jobId, ct);
}
