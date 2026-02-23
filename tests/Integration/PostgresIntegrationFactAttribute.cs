using Xunit;

namespace Integration;

/// <summary>
/// Skips integration tests when PostgreSQL connection string is not configured.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PostgresIntegrationFactAttribute : FactAttribute
{
    private const string EnvVarName = "ConnectionStrings__Db";

    public PostgresIntegrationFactAttribute()
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvVarName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Skip = $"Integration test skipped because {EnvVarName} is not set.";
        }
    }
}
