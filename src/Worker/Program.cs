using Application;
using Infrastructure;
using Serilog;
using Serilog.Formatting.Compact;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<WorkerOptions>(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<IngestionWorker>();

builder.Host.UseSerilog((ctx, cfg) =>
{
    var level = ctx.Configuration["LOG_LEVEL"] ?? "Information";
    cfg.MinimumLevel.Is(Enum.Parse<Serilog.Events.LogEventLevel>(level, true))
       .Enrich.FromLogContext()
       .WriteTo.Console(new RenderedCompactJsonFormatter());
});

var app = builder.Build();
await app.RunAsync();
