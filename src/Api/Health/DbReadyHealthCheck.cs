using Microsoft.Extensions.Configuration;
using Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Api.Health;

public sealed class DbReadyHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionString = DbConnectionFactory.ResolveConnectionString(configuration);
            var normalizedConnectionString = DbConnectionFactory.NormalizePostgresConnectionString(connectionString);
            await using var connection = new NpgsqlConnection(normalizedConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT 1", connection);
            var result = await command.ExecuteScalarAsync(cancellationToken);

            return result is 1
                ? HealthCheckResult.Healthy("database reachable")
                : HealthCheckResult.Unhealthy("database connectivity check returned unexpected result");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"database unreachable: {ex.GetType().Name}");
        }
    }
}
