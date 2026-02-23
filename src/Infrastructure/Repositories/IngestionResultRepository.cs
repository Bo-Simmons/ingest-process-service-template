using Application;
using Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class IngestionResultRepository(IngestionDbContext db) : IIngestionResultRepository
{
    public async Task<IReadOnlyList<ProcessingResultItem>> GetByJobIdAsync(Guid jobId, CancellationToken ct)
    {
        return await db.IngestionResults.AsNoTracking()
            .Where(x => x.JobId == jobId)
            .OrderBy(x => x.EventType)
            .Select(x => new ProcessingResultItem(x.EventType, x.Count))
            .ToListAsync(ct);
    }
}
