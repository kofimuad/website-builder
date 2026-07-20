using Microsoft.EntityFrameworkCore;

namespace WebsiteBuilder.Data;

public interface ITenantStore
{
    Task<Guid?> FindIdBySubdomainAsync(string subdomain, CancellationToken cancellationToken = default);
}

public sealed class TenantStore(WebsiteBuilderDbContext db) : ITenantStore
{
    public async Task<Guid?> FindIdBySubdomainAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        // Tenant is not tenant-owned, so this lookup runs before any tenant is in scope.
        var id = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Subdomain == subdomain)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return id;
    }
}
