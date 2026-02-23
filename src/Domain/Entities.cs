using System.Text.Json;

namespace Domain;

/// <summary>
/// Represents the lifecycle state of an ingestion job.
/// </summary>
public enum IngestionJobStatus
{
    /// <summary>
    /// Job is waiting to be picked up by a worker.
    /// </summary>
    Pending,

    /// <summary>
    /// Job has been claimed and is currently being processed.
    /// </summary>
    Processing,

    /// <summary>
    /// Job finished successfully and results are available.
    /// </summary>
    Succeeded,

    /// <summary>
    /// Job exhausted retries or encountered a terminal error.
    /// </summary>
    Failed
}

/// <summary>
/// Represents one ingestion job row stored in the database.
/// Think of this as the "master record" for one submission.
/// </summary>
public sealed class IngestionJob
{
    /// <summary>
    /// Unique identifier of the job.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Tenant that owns this job.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Optional client-provided key used for idempotent create requests.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Current status of the job.
    /// </summary>
    public IngestionJobStatus Status { get; set; } = IngestionJobStatus.Pending;

    /// <summary>
    /// Number of processing attempts so far.
    /// </summary>
    public int Attempt { get; set; }

    /// <summary>
    /// Timestamp when the job was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when the job was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Earliest time at which the job may be retried.
    /// </summary>
    public DateTimeOffset? AvailableAt { get; set; }

    /// <summary>
    /// Timestamp when a worker claimed the job lock.
    /// </summary>
    public DateTimeOffset? LockedAt { get; set; }

    /// <summary>
    /// Identifier of the worker instance currently holding the lock.
    /// </summary>
    public string? LockedBy { get; set; }

    /// <summary>
    /// Last error message if processing failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Timestamp when processing completed successfully.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>
    /// Raw events attached to this job.
    /// </summary>
    public List<RawEvent> RawEvents { get; set; } = new();

    /// <summary>
    /// Aggregated result rows generated from this job.
    /// </summary>
    public List<IngestionResult> Results { get; set; } = new();
}

/// <summary>
/// Represents one raw event row associated with an ingestion job.
/// </summary>
public sealed class RawEvent
{
    /// <summary>
    /// Surrogate primary key for the raw event.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Foreign key to the parent ingestion job.
    /// </summary>
    public Guid JobId { get; set; }

    /// <summary>
    /// Navigation property to the parent job.
    /// </summary>
    public IngestionJob Job { get; set; } = null!;

    /// <summary>
    /// Tenant that owns this event.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Event type label (for example, "clicked" or "viewed").
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Event occurrence time supplied by the caller.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Raw JSON payload captured exactly as sent by the client.
    /// </summary>
    public JsonElement Payload { get; set; } = JsonSerializer.SerializeToElement(new { });
}

/// <summary>
/// Represents one aggregated result record for a processed job.
/// </summary>
public sealed class IngestionResult
{
    /// <summary>
    /// Surrogate primary key for the result row.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Foreign key to the parent ingestion job.
    /// </summary>
    public Guid JobId { get; set; }

    /// <summary>
    /// Navigation property to the parent job.
    /// </summary>
    public IngestionJob Job { get; set; } = null!;

    /// <summary>
    /// Event type this count belongs to.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Number of events observed for this event type.
    /// </summary>
    public int Count { get; set; }
}
