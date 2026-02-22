using Application;
using Domain;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure;

public sealed class EfIngestionStore(IngestionDbContext db) : IIngestionStore
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

    public void AddJob(IngestionJob job) => db.IngestionJobs.Add(job);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public Task<IngestionJob?> GetJobAsync(Guid jobId, CancellationToken ct) =>
        db.IngestionJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == jobId, ct);

    public Task<bool> JobExistsAsync(Guid jobId, CancellationToken ct) =>
        db.IngestionJobs.AsNoTracking().AnyAsync(x => x.Id == jobId, ct);

    public async Task<IReadOnlyList<ResultItem>> GetResultsAsync(Guid jobId, CancellationToken ct)
    {
        return await db.IngestionResults.AsNoTracking()
            .Where(x => x.JobId == jobId)
            .OrderBy(x => x.EventType)
            .Select(x => new ResultItem(x.EventType, x.Count))
            .ToListAsync(ct);
    }
}
