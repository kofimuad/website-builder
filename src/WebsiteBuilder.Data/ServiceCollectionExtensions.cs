using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWebsiteBuilderData(this IServiceCollection services, string connectionString)
    {
        // One TenantContext instance per request, resolvable through either interface so
        // middleware can set it and the DbContext can read it.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddDbContext<WebsiteBuilderDbContext>(options =>
            options.UseNpgsql(DatabaseUrl.ToNpgsqlConnectionString(connectionString)));

        services.AddScoped<ITenantStore, TenantStore>();

        return services;
    }
}
