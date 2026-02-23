using Application;
using Domain;
using FluentAssertions;
using Xunit;

namespace Unit;

/// <summary>
/// Unit tests for core deterministic processing helper functions.
/// </summary>
public sealed class ProcessingLogicTests
{
    /// <summary>
    /// Verifies event aggregation is case-insensitive and produces stable counts.
    /// </summary>
    [Fact]
    public void AggregateByEventType_ReturnsDeterministicCounts()
    {
        var events = new[]
        {
            new RawEvent { Type = "clicked" },
            new RawEvent { Type = "viewed" },
            new RawEvent { Type = "Clicked" }
        };

        var results = ProcessingLogic.AggregateByEventType(events);

        results.Should().BeEquivalentTo(new[]
        {
            new ResultItem("clicked", 2),
            new ResultItem("viewed", 1)
        });
    }

    /// <summary>
    /// Verifies exponential backoff doubles each attempt for the configured base value.
    /// </summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    public void ComputeBackoff_UsesExponentialGrowth(int attempt, int expectedSeconds)
    {
        var backoff = ProcessingLogic.ComputeBackoff(attempt, 2);
        backoff.TotalSeconds.Should().Be(expectedSeconds);
    }
}
