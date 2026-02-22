using Application;
using Infrastructure;
using Serilog;
using Serilog.Formatting.Compact;

/// <summary>
/// Worker service entry point.
/// Sets up logging, dependency injection, and runs background processing loops.
/// </summary>
var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<WorkerOptions>(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<IngestionWorker>();

builder.Services.AddSerilog((svc, cfg) =>
{
    var level = builder.Configuration["LOG_LEVEL"] ?? "Information";
    cfg.MinimumLevel.Is(Enum.Parse<Serilog.Events.LogEventLevel>(level, true))
       .Enrich.FromLogContext()
       .WriteTo.Console(new RenderedCompactJsonFormatter());
});

var host = builder.Build();
await host.RunAsync();
