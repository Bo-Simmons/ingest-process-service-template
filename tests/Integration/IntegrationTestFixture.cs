using Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Integration;

/// <summary>
/// Shared integration test fixture that boots the real API host against PostgreSQL.
/// </summary>
public sealed class IntegrationTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string EnvVarName = "ConnectionStrings__Db";
    private string? _connectionString;

    public bool ShouldSkip { get; private set; }

    public string SkipReason { get; private set; } = string.Empty;

    public HttpClient Client { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            var connectionString = Environment.GetEnvironmentVariable(EnvVarName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return;
            }

            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Db"] = connectionString,
                ["RUN_MIGRATIONS_ON_STARTUP"] = "false"
            });
        });
    }

    public async Task InitializeAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable(EnvVarName);
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            ShouldSkip = true;
            SkipReason = $"Integration tests skipped because {EnvVarName} is not set.";
            return;
        }

        Client = CreateClient();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IngestionDbContext>();
        await db.Database.MigrateAsync();
        await ResetDatabaseAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        if (ShouldSkip || string.IsNullOrWhiteSpace(_connectionString))
        {
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE TABLE ingestion_results, raw_events, ingestion_jobs RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    public new async Task DisposeAsync()
    {
        if (!ShouldSkip && Client is not null)
        {
            Client.Dispose();
        }

        await base.DisposeAsync();
    }
}
