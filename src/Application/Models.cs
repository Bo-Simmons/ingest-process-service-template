using System.Text.Json;
using Domain;

namespace Application;

public sealed record IngestionEventInput(string Type, DateTimeOffset Timestamp, JsonElement Payload);
public sealed record SubmitIngestionRequest(string TenantId, IReadOnlyList<IngestionEventInput> Events);
public sealed record SubmitIngestionResponse(Guid JobId, bool IsDuplicate);

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

public sealed class WorkerOptions
{
    public int WorkerConcurrency { get; set; } = 2;
    public int MaxAttempts { get; set; } = 5;
    public int BaseBackoffSeconds { get; set; } = 2;
}

public static class ProcessingLogic
{
    public static IReadOnlyList<ResultItem> AggregateByEventType(IEnumerable<RawEvent> events)
    {
        return events
            .GroupBy(e => e.Type, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ResultItem(g.Key, g.Count()))
            .OrderBy(x => x.EventType, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static TimeSpan ComputeBackoff(int attempt, int baseBackoffSeconds)
    {
        var boundedAttempt = Math.Clamp(attempt, 1, 10);
        var seconds = baseBackoffSeconds * Math.Pow(2, boundedAttempt - 1);
        return TimeSpan.FromSeconds(Math.Min(seconds, 300));
    }
}
