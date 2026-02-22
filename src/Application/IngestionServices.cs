using Domain;
using Microsoft.EntityFrameworkCore;

namespace Application;

public interface IIngestionService
{
    Task<SubmitIngestionResponse> SubmitAsync(SubmitIngestionRequest request, string? idempotencyKey, CancellationToken ct);
    Task<JobStatusResponse?> GetStatusAsync(Guid jobId, CancellationToken ct);
    Task<JobResultsResponse?> GetResultsAsync(Guid jobId, CancellationToken ct);
}

public interface IIngestionStore
{
    Task<Guid?> FindJobIdByTenantAndIdempotencyAsync(string tenantId, string idempotencyKey, CancellationToken ct);
    void AddJob(IngestionJob job);
    Task SaveChangesAsync(CancellationToken ct);
    Task<IngestionJob?> GetJobAsync(Guid jobId, CancellationToken ct);
    Task<bool> JobExistsAsync(Guid jobId, CancellationToken ct);
    Task<IReadOnlyList<ResultItem>> GetResultsAsync(Guid jobId, CancellationToken ct);
}

public sealed class IngestionService(IIngestionStore store) : IIngestionService
{
    public async Task<SubmitIngestionResponse> SubmitAsync(SubmitIngestionRequest request, string? idempotencyKey, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existingJobId = await store.FindJobIdByTenantAndIdempotencyAsync(request.TenantId, idempotencyKey, ct);
            if (existingJobId.HasValue)
            {
                return new SubmitIngestionResponse(existingJobId.Value, true);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var job = new IngestionJob
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            IdempotencyKey = idempotencyKey,
            Status = IngestionJobStatus.Pending,
            Attempt = 0,
            CreatedAt = now,
            UpdatedAt = now,
            AvailableAt = now
        };

        foreach (var evt in request.Events)
        {
            job.RawEvents.Add(new RawEvent
            {
                TenantId = request.TenantId,
                Type = evt.Type,
                Timestamp = evt.Timestamp,
                PayloadJson = evt.Payload.GetRawText()
            });
        }

        store.AddJob(job);

        try
        {
            await store.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existingJobId = await store.FindJobIdByTenantAndIdempotencyAsync(request.TenantId, idempotencyKey!, ct);
            if (existingJobId.HasValue)
            {
                return new SubmitIngestionResponse(existingJobId.Value, true);
            }

            throw;
        }

        return new SubmitIngestionResponse(job.Id, false);
    }

    public async Task<JobStatusResponse?> GetStatusAsync(Guid jobId, CancellationToken ct)
    {
        var job = await store.GetJobAsync(jobId, ct);
        if (job is null)
        {
            return null;
        }

        return new JobStatusResponse(job.Id, job.Status.ToString(), job.Attempt, job.CreatedAt, job.UpdatedAt, job.ProcessedAt, job.Error);
    }

    public async Task<JobResultsResponse?> GetResultsAsync(Guid jobId, CancellationToken ct)
    {
        var exists = await store.JobExistsAsync(jobId, ct);
        if (!exists)
        {
            return null;
        }

        var results = await store.GetResultsAsync(jobId, ct);
        return new JobResultsResponse(jobId, results);
    }
}
