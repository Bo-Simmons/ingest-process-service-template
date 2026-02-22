using Application;
using Domain;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// Background worker that continuously claims ingestion jobs, processes them,
/// stores aggregated results, and handles retry/failure behavior.
/// </summary>
public sealed class IngestionWorker(
    IngestionDbContext db,
    IOptions<WorkerOptions> options,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    private readonly WorkerOptions _options = options.Value;
    private readonly string _workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    /// <summary>
    /// Starts the configured number of concurrent processing loops.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = Enumerable.Range(0, Math.Max(1, _options.WorkerConcurrency))
            .Select(_ => RunLoop(stoppingToken));

        await Task.WhenAll(loops);
    }

    /// <summary>
    /// Main poll loop: claim a job, process it, and persist success/failure updates.
    /// </summary>
    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var claimed = await TryClaimJob(ct);
            if (claimed is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                continue;
            }

            try
            {
                var results = ProcessingLogic.AggregateByEventType(claimed.RawEvents);
                db.IngestionResults.RemoveRange(db.IngestionResults.Where(x => x.JobId == claimed.Id));
                foreach (var item in results)
                {
                    db.IngestionResults.Add(new IngestionResult { JobId = claimed.Id, EventType = item.EventType, Count = item.Count });
                }

                claimed.Status = IngestionJobStatus.Succeeded;
                claimed.ProcessedAt = DateTimeOffset.UtcNow;
                claimed.UpdatedAt = DateTimeOffset.UtcNow;
                claimed.LockedAt = null;
                claimed.LockedBy = null;
                claimed.Error = null;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed processing job {JobId}", claimed.Id);
                await HandleFailureAsync(claimed.Id, ex, ct);
            }
        }
    }

    /// <summary>
    /// Attempts to claim one eligible job using a DB transaction and row lock.
    /// Returns null when no work is currently available.
    /// </summary>
    private async Task<IngestionJob?> TryClaimJob(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var sql = $@"
SELECT id FROM ingestion_jobs
WHERE status IN ('Pending','Processing')
  AND (available_at IS NULL OR available_at <= NOW())
  AND (locked_at IS NULL OR locked_at < NOW() - INTERVAL '5 minutes')
ORDER BY created_at
FOR UPDATE SKIP LOCKED
LIMIT 1";

        var id = await db.Database.SqlQueryRaw<Guid?>(sql).FirstOrDefaultAsync(ct);
        if (id is null || id == Guid.Empty)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        var job = await db.IngestionJobs.Include(x => x.RawEvents).FirstAsync(x => x.Id == id, ct);
        job.Status = IngestionJobStatus.Processing;
        job.Attempt += 1;
        job.LockedAt = DateTimeOffset.UtcNow;
        job.LockedBy = _workerId;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return job;
    }

    /// <summary>
    /// Updates job metadata after a processing exception.
    /// Marks terminal failure when max attempts are reached, otherwise schedules retry.
    /// </summary>
    private async Task HandleFailureAsync(Guid jobId, Exception ex, CancellationToken ct)
    {
        var job = await db.IngestionJobs.FirstAsync(x => x.Id == jobId, ct);
        job.LockedAt = null;
        job.LockedBy = null;
        job.UpdatedAt = DateTimeOffset.UtcNow;

        if (job.Attempt >= _options.MaxAttempts)
        {
            job.Status = IngestionJobStatus.Failed;
            job.Error = ex.Message;
            job.AvailableAt = null;
        }
        else
        {
            job.Status = IngestionJobStatus.Pending;
            job.Error = ex.Message;
            job.AvailableAt = DateTimeOffset.UtcNow.Add(ProcessingLogic.ComputeBackoff(job.Attempt, _options.BaseBackoffSeconds));
        }

        await db.SaveChangesAsync(ct);
    }
}
