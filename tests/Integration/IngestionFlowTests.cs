using System.Linq;
using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Application;
using Domain;
using FluentAssertions;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Sdk;

namespace Integration;

/// <summary>
/// End-to-end integration tests for API ingestion flow using PostgreSQL.
/// </summary>
public sealed class IngestionFlowTests : IClassFixture<IntegrationTestFixture>, IAsyncLifetime
{
    private readonly IntegrationTestFixture _fixture;

    /// <summary>
    /// Stores test server fixture reference for creating HTTP clients and service scopes.
    /// </summary>
    public IngestionFlowTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Verifies post-ingestion, process, status, and results retrieval flow.
    /// </summary>
    [PostgresIntegrationFact]
    public async Task PostIngestion_ProcessJob_GetResults()
    {
        EnsureIntegrationConfigured();

        var client = _fixture.Client;

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
        var responseBody = await post.Content.ReadAsStringAsync();
        post.StatusCode.Should().Be(HttpStatusCode.Accepted, "POST body: {0}", responseBody);
        var postBody = await post.Content.ReadFromJsonAsync<JobCreateResponse>();
        postBody.Should().NotBeNull();

        await SimulateWorker(postBody!.JobId);

        var statusResponse = await client.GetAsync($"/v1/ingestions/{postBody.JobId}");
        var statusResponseBody = await statusResponse.Content.ReadAsStringAsync();
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK, "GET status body: {0}", statusResponseBody);
        var status = await statusResponse.Content.ReadFromJsonAsync<JobStatusResponse>();
        status.Should().NotBeNull();
        status!.Status.Should().Be(nameof(IngestionJobStatus.Succeeded));

        var resultsResponse = await client.GetAsync($"/v1/results/{postBody.JobId}");
        var resultsResponseBody = await resultsResponse.Content.ReadAsStringAsync();
        resultsResponse.StatusCode.Should().Be(HttpStatusCode.OK, "GET results body: {0}", resultsResponseBody);
        var results = await resultsResponse.Content.ReadFromJsonAsync<JobResultsResponse>();
        results.Should().NotBeNull();
        results!.Results.Should().ContainEquivalentOf(new Api.Contracts.ResultItem("clicked", 2));
        results.Results.Should().ContainEquivalentOf(new Api.Contracts.ResultItem("viewed", 1));
    }

    [PostgresIntegrationFact]
    public void IdempotencyLookup_UsesSnakeCaseColumnsInGeneratedSql()
    {
        EnsureIntegrationConfigured();

        using var scope = _fixture.Services.CreateScope();
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

    private void EnsureIntegrationConfigured()
    {
        if (_fixture.ShouldSkip)
        {
            throw new SkipException(_fixture.SkipReason);
        }
    }

    /// <summary>
    /// Simulates worker behavior directly in test scope so test can validate API contracts.
    /// </summary>
    private async Task SimulateWorker(Guid jobId)
    {
        using var scope = _fixture.Services.CreateScope();
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
