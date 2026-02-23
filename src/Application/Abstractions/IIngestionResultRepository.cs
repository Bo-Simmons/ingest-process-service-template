using Application;

namespace Application.Abstractions;

public interface IIngestionResultRepository
{
    Task<IReadOnlyList<ProcessingResultItem>> GetByJobIdAsync(Guid jobId, CancellationToken ct);
}
