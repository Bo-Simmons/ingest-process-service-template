using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure;

/// <summary>
/// Design-time factory for EF Core tools (migrations/list/update).
/// </summary>
public sealed class IngestionDbContextFactory : IDesignTimeDbContextFactory<IngestionDbContext>
{
    public IngestionDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Db")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings:Db")
            ?? throw new InvalidOperationException(
                "Design-time database connection string is missing. Set ConnectionStrings__Db environment variable.");

        var optionsBuilder = new DbContextOptionsBuilder<IngestionDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString, x => x.MigrationsAssembly("Infrastructure"));

        return new IngestionDbContext(optionsBuilder.Options);
    }
}
