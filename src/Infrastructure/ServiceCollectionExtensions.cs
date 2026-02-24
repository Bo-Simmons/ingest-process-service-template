using Application;
using Application.Abstractions;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

/// <summary>
/// Registers infrastructure services (database + repositories) into dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds infrastructure dependencies using configured database settings.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        var rawConnectionString = config["ConnectionStrings__Db"]
            ?? config.GetConnectionString("Db")
            ?? config["DATABASE_URL"];

        var normalizedConnectionString = DbConnectionFactory.NormalizePostgresConnectionString(
            rawConnectionString
            ?? throw new InvalidOperationException("ConnectionStrings:Db or DATABASE_URL must be configured."));

        services.AddDbContext<IngestionDbContext>(options =>
            options.UseNpgsql(normalizedConnectionString, npgsql => npgsql.MigrationsAssembly("Infrastructure")));

        services.AddScoped<IIngestionJobRepository, IngestionJobRepository>();
        services.AddScoped<IRawEventRepository, RawEventRepository>();
        services.AddScoped<IIngestionResultRepository, IngestionResultRepository>();
        services.AddScoped<IIngestionService, IngestionService>();

        return services;
    }
}
