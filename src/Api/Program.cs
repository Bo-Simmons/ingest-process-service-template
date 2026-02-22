using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Application;
using Domain;
using Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;

/// <summary>
/// API entry point.
/// Configures dependencies, middleware, endpoints, and database migrations.
/// </summary>
var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<WorkerOptions>(builder.Configuration);
builder.Host.UseSerilog((ctx, cfg) =>
{
    var level = ctx.Configuration["LOG_LEVEL"] ?? "Information";
    cfg.MinimumLevel.Is(Enum.Parse<Serilog.Events.LogEventLevel>(level, true))
       .Enrich.FromLogContext()
       .WriteTo.Console(new RenderedCompactJsonFormatter());
});

builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<ReadyHealthCheck>("ready");

var app = builder.Build();

// Correlation-id middleware so logs and responses can be tied to one request.
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers.TryGetValue("X-Correlation-Id", out var values)
        ? values.ToString()
        : Guid.NewGuid().ToString("n");

    context.Response.Headers["X-Correlation-Id"] = correlationId;
    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
    {
        await next();
    }
});

app.UseExceptionHandler();

// Apply DB migrations at startup to ensure schema exists before serving traffic.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IngestionDbContext>();
    db.Database.Migrate();
}

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

/// <summary>
/// Creates a new ingestion job, with optional idempotency support.
/// </summary>
app.MapPost("/v1/ingestions", async (
    [FromBody] IngestionRequest request,
    [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
    IngestionDbContext db,
    CancellationToken ct) =>
{
    var errors = request.Validate();
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    if (!string.IsNullOrWhiteSpace(idempotencyKey))
    {
        var existing = await db.IngestionJobs
            .AsNoTracking()
            .Where(x => x.TenantId == request.TenantId && x.IdempotencyKey == idempotencyKey)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (existing != Guid.Empty)
        {
            return Results.Accepted($"/v1/ingestions/{existing}", new { jobId = existing });
        }
    }

    var now = DateTimeOffset.UtcNow;
    var job = new IngestionJob
    {
        Id = Guid.NewGuid(),
        TenantId = request.TenantId,
        IdempotencyKey = idempotencyKey,
        Status = IngestionJobStatus.Pending,
        Attempt = 0,
        CreatedAt = now,
        UpdatedAt = now,
        AvailableAt = now
    };

    foreach (var evt in request.Events)
    {
        job.RawEvents.Add(new RawEvent
        {
            TenantId = request.TenantId,
            Type = evt.Type,
            Timestamp = evt.Timestamp,
            PayloadJson = evt.Payload.GetRawText()
        });
    }

    db.IngestionJobs.Add(job);

    try
    {
        await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(idempotencyKey))
    {
        var existing = await db.IngestionJobs
            .AsNoTracking()
            .Where(x => x.TenantId == request.TenantId && x.IdempotencyKey == idempotencyKey)
            .Select(x => x.Id)
            .FirstAsync(ct);
        return Results.Accepted($"/v1/ingestions/{existing}", new { jobId = existing });
    }

    return Results.Accepted($"/v1/ingestions/{job.Id}", new { jobId = job.Id });
});

/// <summary>
/// Returns current state for a specific ingestion job.
/// </summary>
app.MapGet("/v1/ingestions/{jobId:guid}", async (Guid jobId, IngestionDbContext db, CancellationToken ct) =>
{
    var job = await db.IngestionJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == jobId, ct);
    if (job is null)
    {
        return Results.NotFound(new ProblemDetails { Title = "Job not found", Status = 404 });
    }

    return Results.Ok(new JobStatusResponse(job.Id, job.Status.ToString(), job.Attempt, job.CreatedAt, job.UpdatedAt, job.ProcessedAt, job.Error));
});

/// <summary>
/// Returns aggregated result rows for a processed ingestion job.
/// </summary>
app.MapGet("/v1/results/{jobId:guid}", async (Guid jobId, IngestionDbContext db, CancellationToken ct) =>
{
    var exists = await db.IngestionJobs.AsNoTracking().AnyAsync(x => x.Id == jobId, ct);
    if (!exists)
    {
        return Results.NotFound(new ProblemDetails { Title = "Job not found", Status = 404 });
    }

    var results = await db.IngestionResults.AsNoTracking()
        .Where(x => x.JobId == jobId)
        .OrderBy(x => x.EventType)
        .Select(x => new ResultItem(x.EventType, x.Count))
        .ToListAsync(ct);

    return Results.Ok(new JobResultsResponse(jobId, results));
});

app.Run();

/// <summary>
/// Exposed for integration tests that need to bootstrap the API host.
/// </summary>
public partial class Program;

/// <summary>
/// Incoming API request for creating an ingestion job.
/// </summary>
public sealed record IngestionRequest(string TenantId, IReadOnlyList<IngestionEventRequest> Events)
{
    /// <summary>
    /// Validates required request fields and returns errors in ProblemDetails format.
    /// </summary>
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(TenantId))
        {
            errors["tenantId"] = ["tenantId is required"];
        }

        if (Events is null || Events.Count == 0)
        {
            errors["events"] = ["at least one event is required"];
            return errors;
        }

        var eventErrors = new List<string>();
        for (var i = 0; i < Events.Count; i++)
        {
            var item = Events[i];
            if (string.IsNullOrWhiteSpace(item.Type))
            {
                eventErrors.Add($"events[{i}].type is required");
            }

            if (item.Timestamp == default)
            {
                eventErrors.Add($"events[{i}].timestamp must be ISO-8601 date");
            }
        }

        if (eventErrors.Count > 0)
        {
            errors["events"] = eventErrors.ToArray();
        }

        return errors;
    }
}

/// <summary>
/// One event item inside an ingestion request.
/// </summary>
public sealed record IngestionEventRequest(string Type, DateTimeOffset Timestamp, JsonElement Payload);

/// <summary>
/// Readiness health check verifies that the API can communicate with the database.
/// </summary>
public sealed class ReadyHealthCheck(IngestionDbContext db) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    /// <summary>
    /// Executes the readiness probe by performing a simple DB connectivity check.
    /// </summary>
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var canConnect = await db.Database.CanConnectAsync(cancellationToken);
        return canConnect
            ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("database reachable")
            : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("database unreachable");
    }
}
