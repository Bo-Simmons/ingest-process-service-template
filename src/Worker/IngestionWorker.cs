using Application;
using Domain;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// Background worker that continuously claims ingestion jobs, processes them,
/// stores aggregated results, and handles retry/failure behavior.
/// </summary>
public sealed class IngestionWorker(
    IServiceScopeFactory scopeFactory,
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
        logger.LogInformation("Ingestion worker started with concurrency {Concurrency}", Math.Max(1, _options.WorkerConcurrency));

        var loops = Enumerable.Range(0, Math.Max(1, _options.WorkerConcurrency))
            .Select(_ => RunLoop(stoppingToken));

        try
        {
            await Task.WhenAll(loops);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Worker loop crashed unexpectedly");
        }
    }

    /// <summary>
    /// Main poll loop: claim a job, process it, and persist success/failure updates.
    /// </summary>
    private async Task RunLoop(CancellationToken ct)
    {
        var basePollSeconds = Math.Max(1, _options.WorkerPollSeconds);
        var maxIdleBackoffSeconds = Math.Max(basePollSeconds, _options.WorkerIdleBackoffMaxSeconds);
        var idleDelaySeconds = basePollSeconds;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IngestionDbContext>();

                var claimed = await TryClaimJob(db, ct);
                if (claimed is null)
                {
                    if (_options.WorkerLogNoJobs && logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("No jobs available; polling again in {DelaySeconds}s", idleDelaySeconds);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(idleDelaySeconds), ct);
                    idleDelaySeconds = Math.Min(idleDelaySeconds * 2, maxIdleBackoffSeconds);
                    continue;
                }

                idleDelaySeconds = basePollSeconds;
                logger.LogInformation("Claimed job {JobId} for tenant {TenantId}", claimed.Id, claimed.TenantId);

                var startedAt = DateTimeOffset.UtcNow;
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

                    var elapsed = DateTimeOffset.UtcNow - startedAt;
                    logger.LogInformation("Job {JobId} completed with status {Status} in {ElapsedMs}ms", claimed.Id, IngestionJobStatus.Succeeded, elapsed.TotalMilliseconds);
                }
                catch (Exception ex)
                {
                    await HandleFailureAsync(db, claimed.Id, ex, _options, logger, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker loop iteration failed");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.BaseBackoffSeconds)), ct);
            }
        }
    }

    /// <summary>
    /// Attempts to claim one eligible job using a DB transaction and row lock.
    /// Returns null when no work is currently available.
    /// </summary>
    private async Task<IngestionJob?> TryClaimJob(IngestionDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var sql = """
SELECT id AS "Value"
FROM ingestion_jobs
WHERE status IN ('Pending','Processing')
  AND (available_at IS NULL OR available_at <= NOW())
  AND (locked_at IS NULL OR locked_at < NOW() - INTERVAL '5 minutes')
ORDER BY created_at
FOR UPDATE SKIP LOCKED
LIMIT 1
""";

        var id = await db.Database.SqlQueryRaw<Guid?>(sql).FirstOrDefaultAsync(ct);
        if (id is null || id == Guid.Empty)
        {
            return null;
        }

        var job = await db.IngestionJobs
            .Include(x => x.RawEvents)
            .FirstAsync(x => x.Id == id, ct);

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
    private static async Task HandleFailureAsync(
        IngestionDbContext db,
        Guid jobId,
        Exception ex,
        WorkerOptions options,
        ILogger logger,
        CancellationToken ct)
    {
        var job = await db.IngestionJobs.FirstAsync(x => x.Id == jobId, ct);
        job.LockedAt = null;
        job.LockedBy = null;
        job.UpdatedAt = DateTimeOffset.UtcNow;

        if (job.Attempt >= options.MaxAttempts)
        {
            job.Status = IngestionJobStatus.Failed;
            job.Error = ex.Message;
            job.AvailableAt = null;

            logger.LogError(ex, "Job {JobId} failed terminally with status {Status} after {Attempt} attempts", job.Id, IngestionJobStatus.Failed, job.Attempt);
        }
        else
        {
            var retryDelay = ProcessingLogic.ComputeBackoff(job.Attempt, options.BaseBackoffSeconds);
            var nextRetryAt = DateTimeOffset.UtcNow.Add(retryDelay);

            job.Status = IngestionJobStatus.Pending;
            job.Error = ex.Message;
            job.AvailableAt = nextRetryAt;

            logger.LogWarning(
                ex,
                "Job {JobId} failed on attempt {Attempt}; retrying at {NextAvailableAt} in {RetryBackoffSeconds}s",
                job.Id,
                job.Attempt,
                nextRetryAt,
                retryDelay.TotalSeconds);
        }

        await db.SaveChangesAsync(ct);
    }
}
