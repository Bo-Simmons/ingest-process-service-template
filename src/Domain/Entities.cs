namespace Domain;

public enum IngestionJobStatus
{
    Pending,
    Processing,
    Succeeded,
    Failed
}

public sealed class IngestionJob
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public IngestionJobStatus Status { get; set; } = IngestionJobStatus.Pending;
    public int Attempt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? AvailableAt { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public string? LockedBy { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public List<RawEvent> RawEvents { get; set; } = new();
    public List<IngestionResult> Results { get; set; } = new();
}

public sealed class RawEvent
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public IngestionJob Job { get; set; } = null!;
    public string TenantId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string PayloadJson { get; set; } = "{}";
}

public sealed class IngestionResult
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public IngestionJob Job { get; set; } = null!;
    public string EventType { get; set; } = string.Empty;
    public int Count { get; set; }
}
