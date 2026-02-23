using Application.Abstractions;
using Domain;

namespace Application;

public interface IIngestionService
{
    Task<SubmitIngestionResponse> SubmitAsync(SubmitIngestionRequest request, string? idempotencyKey, CancellationToken ct);
    Task<JobStatusDto?> GetStatusAsync(Guid jobId, CancellationToken ct);
    Task<JobResultsDto?> GetResultsAsync(Guid jobId, CancellationToken ct);
}

public sealed class IngestionService(
    IIngestionJobRepository ingestionJobRepository,
    IRawEventRepository rawEventRepository,
    IIngestionResultRepository ingestionResultRepository) : IIngestionService
{
    public async Task<SubmitIngestionResponse> SubmitAsync(SubmitIngestionRequest request, string? idempotencyKey, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existingJobId = await ingestionJobRepository.FindJobIdByTenantAndIdempotencyAsync(request.TenantId, idempotencyKey, ct);
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

        ingestionJobRepository.Add(job);

        var rawEvents = request.Events.Select(evt => new RawEvent
        {
            JobId = job.Id,
            TenantId = request.TenantId,
            Type = evt.Type,
            Timestamp = evt.Timestamp,
            Payload = evt.Payload.Clone()
        });

        rawEventRepository.AddRange(rawEvents);

        var saveResult = await ingestionJobRepository.SaveSubmissionAsync(request.TenantId, idempotencyKey, ct);
        if (saveResult.Outcome == SaveSubmissionOutcome.Duplicate && saveResult.ExistingJobId.HasValue)
        {
            return new SubmitIngestionResponse(saveResult.ExistingJobId.Value, true);
        }

        return new SubmitIngestionResponse(job.Id, false);
    }

    public async Task<JobStatusDto?> GetStatusAsync(Guid jobId, CancellationToken ct)
    {
        var job = await ingestionJobRepository.GetByIdAsync(jobId, ct);
        if (job is null)
        {
            return null;
        }

        return new JobStatusDto(job.Id, job.Status.ToString(), job.Attempt, job.CreatedAt, job.UpdatedAt, job.ProcessedAt, job.Error);
    }

    public async Task<JobResultsDto?> GetResultsAsync(Guid jobId, CancellationToken ct)
    {
        var exists = await ingestionJobRepository.ExistsAsync(jobId, ct);
        if (!exists)
        {
            return null;
        }

        var results = await ingestionResultRepository.GetByJobIdAsync(jobId, ct);
        return new JobResultsDto(jobId, results);
    }
}
