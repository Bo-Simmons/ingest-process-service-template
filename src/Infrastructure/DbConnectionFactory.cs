using Microsoft.Extensions.Configuration;

namespace Infrastructure;

/// <summary>
/// Builds the database connection string from configuration.
/// Supports both ConnectionStrings:Db and Heroku-style DATABASE_URL.
/// </summary>
public static class DbConnectionFactory
{
    /// <summary>
    /// Resolves the connection string to use for EF Core.
    /// Priority: explicit ConnectionStrings:Db, then DATABASE_URL.
    /// </summary>
    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var direct = configuration.GetConnectionString("Db");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        var databaseUrl = configuration["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            throw new InvalidOperationException("ConnectionStrings:Db or DATABASE_URL must be configured.");
        }

        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var db = uri.AbsolutePath.Trim('/');

        return $"Host={uri.Host};Port={uri.Port};Database={db};Username={user};Password={pass};SSL Mode=Require;Trust Server Certificate=true";
    }
}
