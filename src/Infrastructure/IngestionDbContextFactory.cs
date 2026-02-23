using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Infrastructure;

/// <summary>
/// Design-time factory for EF Core tools (migrations/list/update).
/// </summary>
public sealed class IngestionDbContextFactory : IDesignTimeDbContextFactory<IngestionDbContext>
{
    public IngestionDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var connectionString = DbConnectionFactory.ResolveConnectionString(configuration);

        var optionsBuilder = new DbContextOptionsBuilder<IngestionDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString, x => x.MigrationsAssembly("Infrastructure"))
            .UseSnakeCaseNamingConvention();

        return new IngestionDbContext(optionsBuilder.Options);
    }
}
