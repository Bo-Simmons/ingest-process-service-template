namespace Application;

/// <summary>
/// Represents one aggregated result row: event type + how many times it appeared.
/// </summary>
public sealed record ProcessingResultItem(string EventType, int Count);
