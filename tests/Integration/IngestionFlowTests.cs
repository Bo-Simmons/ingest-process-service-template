using System.Linq;
using System.Net.Http.Json;
using Api.Contracts;
using Application;
using Domain;
using FluentAssertions;
using Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration;

/// <summary>
/// End-to-end integration tests for API ingestion flow using an in-memory SQLite DB.
/// </summary>
public sealed class IngestionFlowTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    /// <summary>
    /// Stores test server factory reference for creating HTTP clients and service scopes.
    /// </summary>
    public IngestionFlowTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Verifies post-ingestion, process, status, and results retrieval flow.
    /// </summary>
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
        results!.Results.Should().ContainEquivalentOf(new Api.Contracts.ResultItem("clicked", 2));
        results.Results.Should().ContainEquivalentOf(new Api.Contracts.ResultItem("viewed", 1));
    }

    [Fact]
    public void IdempotencyLookup_UsesSnakeCaseColumnsInGeneratedSql()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IngestionDbContext>();

        var sql = db.IngestionJobs
            .AsNoTracking()
            .Where(x => x.TenantId == "tenant-a" && x.IdempotencyKey == "idem-1")
            .Select(x => x.Id)
            .ToQueryString();

        sql.Should().Contain("ingestion_jobs");
        sql.Should().Contain("tenant_id");
        sql.Should().Contain("idempotency_key");
        sql.Should().Contain("id");
        sql.Should().NotContain("\"TenantId\"");
        sql.Should().NotContain("\"IdempotencyKey\"");
        sql.Should().NotContain("\"Id\"");
    }

    /// <summary>
    /// Simulates worker behavior directly in test scope so test can validate API contracts.
    /// </summary>
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

}

/// <summary>
/// Custom WebApplicationFactory that swaps PostgreSQL with in-memory SQLite for tests.
/// </summary>
public sealed class TestApiFactory : WebApplicationFactory<Program>, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    /// <summary>
    /// Reconfigures DI services for test hosting and ensures schema creation.
    /// </summary>
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

    /// <summary>
    /// Cleans up SQLite connection when test host is disposed.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
    }
}
