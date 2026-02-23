using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Infrastructure;

/// <summary>
/// Builds the database connection string from configuration.
/// Supports both ConnectionStrings:Db and Heroku-style DATABASE_URL.
/// </summary>
public static class DbConnectionFactory
{
    /// <summary>
    /// Normalizes a Postgres connection string into Npgsql key/value format.
    /// Supports URL form (postgres:// or postgresql://) and key/value form.
    /// </summary>
    public static string NormalizePostgresConnectionString(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new InvalidOperationException("Postgres connection string cannot be empty.");
        }

        var trimmed = input.Trim();
        if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var uri = new Uri(trimmed);
        var userInfo = uri.UserInfo.Split(':', 2);
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.Trim('/'),
            Username = username,
            Password = password,
            SslMode = ResolveSslMode(uri),
            TrustServerCertificate = true
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// Resolves the connection string to use for EF Core.
    /// Priority: explicit ConnectionStrings:Db, then DATABASE_URL.
    /// </summary>
    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var direct = configuration.GetConnectionString("Db");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return NormalizePostgresConnectionString(direct);
        }

        var databaseUrl = configuration["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            throw new InvalidOperationException("ConnectionStrings:Db or DATABASE_URL must be configured.");
        }

        return NormalizePostgresConnectionString(databaseUrl);
    }

    private static SslMode ResolveSslMode(Uri uri)
    {
        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
        {
            return SslMode.Require;
        }

        var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var tokens = part.Split('=', 2);
            if (!tokens[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tokens.Length < 2 || string.IsNullOrWhiteSpace(tokens[1]))
            {
                return SslMode.Require;
            }

            var normalized = tokens[1].Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal);
            if (Enum.TryParse<SslMode>(normalized, true, out var sslMode))
            {
                return sslMode;
            }

            return SslMode.Require;
        }

        return SslMode.Require;
    }
}
