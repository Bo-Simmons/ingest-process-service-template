using Application;
using Domain;
using FluentAssertions;

namespace Unit;

public sealed class ProcessingLogicTests
{
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
