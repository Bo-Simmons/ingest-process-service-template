using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddDbContext<IngestionDbContext>(opt => opt.UseNpgsql(connectionString));
        return services;
    }
}
