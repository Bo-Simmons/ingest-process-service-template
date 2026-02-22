using System.Net.Http.Json;
using Application;
using Domain;
using FluentAssertions;
using Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Integration;

// SQLite fallback integration test to avoid Docker dependency in constrained CI.
public sealed class IngestionFlowTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public IngestionFlowTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostIngestion_ProcessJob_GetResults()
    {
        var client = _factory.CreateClient();

        var payload = new
        {
            tenantId = "tenant-a",
            events = new[]
            {
                new { type = "clicked", timestamp = DateTimeOffset.UtcNow, payload = new { page = "home" } },
                new { type = "clicked", timestamp = DateTimeOffset.UtcNow, payload = new { page = "pricing" } },
                new { type = "viewed", timestamp = DateTimeOffset.UtcNow, payload = new { page = "home" } }
            }
        };

        var post = await client.PostAsJsonAsync("/v1/ingestions", payload);
        post.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);
        var postBody = await post.Content.ReadFromJsonAsync<JobCreateResponse>();
        postBody.Should().NotBeNull();

        await SimulateWorker(postBody!.JobId);

        var status = await client.GetFromJsonAsync<JobStatusResponse>($"/v1/ingestions/{postBody.JobId}");
        status.Should().NotBeNull();
        status!.Status.Should().Be(nameof(IngestionJobStatus.Succeeded));

        var results = await client.GetFromJsonAsync<JobResultsResponse>($"/v1/results/{postBody.JobId}");
        results.Should().NotBeNull();
        results!.Results.Should().ContainEquivalentOf(new ResultItem("clicked", 2));
        results.Results.Should().ContainEquivalentOf(new ResultItem("viewed", 1));
    }

    private async Task SimulateWorker(Guid jobId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IngestionDbContext>();
        var job = await db.IngestionJobs.Include(x => x.RawEvents).FirstAsync(x => x.Id == jobId);

        var results = ProcessingLogic.AggregateByEventType(job.RawEvents);
        foreach (var item in results)
        {
            db.IngestionResults.Add(new IngestionResult { JobId = jobId, EventType = item.EventType, Count = item.Count });
        }

        job.Status = IngestionJobStatus.Succeeded;
        job.ProcessedAt = DateTimeOffset.UtcNow;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    private sealed record JobCreateResponse(Guid JobId);
}

public sealed class TestApiFactory : WebApplicationFactory<Program>, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();
        builder.ConfigureServices(services =>
        {
            var descriptor = services.Single(d => d.ServiceType == typeof(DbContextOptions<IngestionDbContext>));
            services.Remove(descriptor);
            services.AddDbContext<IngestionDbContext>(opt => opt.UseSqlite(_connection));

            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IngestionDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
    }
}
