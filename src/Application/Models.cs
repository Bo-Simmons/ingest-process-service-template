using System.Text.Json;
using Domain;

namespace Application;

/// <summary>
/// Represents one incoming event from the API payload before it is stored in the database.
/// Think of this as the "raw package" the worker will later open and process.
/// </summary>
public sealed record IngestionEventInput(string Type, DateTimeOffset Timestamp, JsonElement Payload);

/// <summary>
/// Represents the full request that asks the system to create a new ingestion job.
/// It includes the tenant identifier and the list of events to process.
/// </summary>
public sealed record SubmitIngestionRequest(string TenantId, IReadOnlyList<IngestionEventInput> Events);

/// <summary>
/// Represents the response after trying to create an ingestion job.
/// JobId identifies the job, and IsDuplicate tells us if we reused an existing job.
/// </summary>
public sealed record SubmitIngestionResponse(Guid JobId, bool IsDuplicate);

/// <summary>
/// Represents the current processing status of a job.
/// This is what callers use to poll for progress.
/// </summary>
public sealed record JobStatusResponse(
    Guid JobId,
    string Status,
    int Attempt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ProcessedAt,
    string? Error);

/// <summary>
/// Represents one aggregated result row: event type + how many times it appeared.
/// </summary>
public sealed record ResultItem(string EventType, int Count);

/// <summary>
/// Represents the complete list of result rows for a given job.
/// </summary>
public sealed record JobResultsResponse(Guid JobId, IReadOnlyList<ResultItem> Results);

/// <summary>
/// Represents worker runtime configuration values loaded from settings.
/// These knobs control parallelism and retry behavior.
/// </summary>
public sealed class WorkerOptions
{
    /// <summary>
    /// Number of concurrent processing loops to run inside one worker process.
    /// </summary>
    public int WorkerConcurrency { get; set; } = 2;

    /// <summary>
    /// Maximum number of attempts before a job is marked as failed permanently.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Base delay (in seconds) used by exponential backoff between retries.
    /// </summary>
    public int BaseBackoffSeconds { get; set; } = 2;
}

/// <summary>
/// Contains reusable processing rules used by both worker and tests.
/// </summary>
public static class ProcessingLogic
{
    /// <summary>
    /// Groups events by type (case-insensitive), counts each group, and returns stable sorted output.
    /// This makes result lists deterministic and easier to test.
    /// </summary>
    public static IReadOnlyList<ResultItem> AggregateByEventType(IEnumerable<RawEvent> events)
    {
        return events
            .GroupBy(e => e.Type, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ResultItem(g.Key, g.Count()))
            .OrderBy(x => x.EventType, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Computes an exponential retry delay based on attempt number and base seconds.
    /// Delay is clamped to avoid extreme values (max 5 minutes).
    /// </summary>
    public static TimeSpan ComputeBackoff(int attempt, int baseBackoffSeconds)
    {
        var boundedAttempt = Math.Clamp(attempt, 1, 10);
        var seconds = baseBackoffSeconds * Math.Pow(2, boundedAttempt - 1);
        return TimeSpan.FromSeconds(Math.Min(seconds, 300));
    }
}
