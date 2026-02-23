using Application;
using Application.Abstractions;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace Infrastructure;

/// <summary>
/// Registers infrastructure services (database, EF Core context) into dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds infrastructure dependencies using configured database settings.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = DbConnectionFactory.ResolveConnectionString(configuration);
        var normalizedConnectionString = DbConnectionFactory.NormalizePostgresConnectionString(connectionString);

        services.AddDbContext<IngestionDbContext>(opt =>
            opt.UseNpgsql(normalizedConnectionString, npgsql => npgsql.MigrationsAssembly("Infrastructure")));

        services.AddScoped<IIngestionJobRepository, IngestionJobRepository>();
        services.AddScoped<IRawEventRepository, RawEventRepository>();
        services.AddScoped<IIngestionResultRepository, IngestionResultRepository>();
        services.AddScoped<IIngestionService, IngestionService>();
        return services;
    }
}
