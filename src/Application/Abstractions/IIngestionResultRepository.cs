using Application;

namespace Application.Abstractions;

public interface IIngestionResultRepository
{
    Task<IReadOnlyList<ResultItem>> GetByJobIdAsync(Guid jobId, CancellationToken ct);
}
