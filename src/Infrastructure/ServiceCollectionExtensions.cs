using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = DbConnectionFactory.ResolveConnectionString(configuration);
        services.AddDbContext<IngestionDbContext>(opt => opt.UseNpgsql(connectionString));
        return services;
    }
}
