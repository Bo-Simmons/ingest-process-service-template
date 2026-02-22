namespace Api.Contracts;

public sealed record JobCreateResponse(Guid JobId);

public sealed record JobStatusResponse(
    Guid JobId,
    string Status,
    int Attempt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ProcessedAt,
    string? Error);

public sealed record ResultItem(string EventType, int Count);

public sealed record JobResultsResponse(Guid JobId, IReadOnlyList<ResultItem> Results);
