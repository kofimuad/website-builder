using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Data;

namespace WebsiteBuilder.Web.Shop;

/// <summary>
/// Reads the resolved tenant's catalog. Every query here goes through the tenant filter, so a
/// product can only ever be read under the tenant that owns it — the same gate that protects
/// leads and sites.
/// </summary>
public sealed class ShopCatalog(WebsiteBuilderDbContext db)
{
    /// <summary>
    /// What a visitor may see: available products only, in the owner's order. Name breaks ties so
    /// the grid does not shuffle between page loads.
    /// </summary>
    public Task<List<Product>> ForVisitorsAsync(int? take = null, CancellationToken cancellationToken = default)
    {
        var query = db.Products
            .AsNoTracking()
            .Where(p => p.IsAvailable)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .AsQueryable();

        if (take is > 0)
        {
            query = query.Take(take.Value);
        }

        return query.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// One product by its public address. Unavailable products are not found rather than hidden:
    /// a page that 404s cannot be linked to from a stale search result and still take an order.
    /// </summary>
    public Task<Product?> BySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == slug && p.IsAvailable, cancellationToken);

    /// <summary>The products behind a set of cart lines, in one query rather than one per line.</summary>
    public async Task<Dictionary<Guid, Product>> ByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var products = await db.Products
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.IsAvailable)
            .ToListAsync(cancellationToken);

        return products.ToDictionary(p => p.Id);
    }
}
