using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Data;

/// <summary>Used only by `dotnet ef` at design time; no tenant is in scope when building migrations.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WebsiteBuilderDbContext>
{
    public WebsiteBuilderDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Host=localhost;Port=5432;Database=websitebuilder;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<WebsiteBuilderDbContext>()
            .UseNpgsql(DatabaseUrl.ToNpgsqlConnectionString(connectionString))
            .Options;

        return new WebsiteBuilderDbContext(options, new TenantContext());
    }
}
