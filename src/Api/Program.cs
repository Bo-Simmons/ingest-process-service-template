using Api.Health;
using Application;
using Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;

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
builder.Services.AddControllers();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck("live", () => HealthCheckResult.Healthy())
    .AddCheck<DbReadyHealthCheck>("ready");

var app = builder.Build();

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

using (var scope = app.Services.CreateScope())
{
    var shouldRunMigrations = builder.Configuration.GetValue<bool>("RUN_MIGRATIONS_ON_STARTUP");
    if (shouldRunMigrations)
    {
        var db = scope.ServiceProvider.GetRequiredService<IngestionDbContext>();
        db.Database.Migrate();
    }
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = r => r.Name == "live" });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Name == "ready" });
app.MapControllers();

app.Run();

public partial class Program;
