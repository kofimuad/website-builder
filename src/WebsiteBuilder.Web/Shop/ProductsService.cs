using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Caching;

namespace WebsiteBuilder.Web.Shop;

/// <summary>
/// The owner's side of the catalog: everything the products page in the builder does.
/// <para>
/// Every method assumes the tenant is already in scope — <c>SiteManagementService.LoadAsync</c> is
/// the gate that put it there, exactly as it is for the editor and the leads inbox. The query
/// filter does the rest, so none of these can reach another business's products.
/// </para>
/// </summary>
public sealed class ProductsService(
    WebsiteBuilderDbContext db,
    ITenantContext tenantContext,
    IOutputCacheStore cache)
{
    private const int MaxSlugLength = 100;

    /// <summary>
    /// Drops the tenant's cached home page after any change to the catalog.
    /// <para>
    /// This is the other half of "products are live the moment they are saved". They are — but the
    /// published site's home page is output-cached for five minutes and tagged by tenant, and only
    /// <c>SitePublisher</c> was evicting that tag. So an owner who added a price, or a whole
    /// product, saw the products page say "Saved" and their own website carry on showing the old
    /// catalog, with no publish available to force it through. That is the "the price is not
    /// showing" report: the price was saved, the page was stale.
    /// </para>
    /// <para>
    /// The shop's own pages (<c>/shop</c>, <c>/products/{slug}</c>, <c>/cart</c>) are deliberately
    /// not cached, which is why the same price was visible there all along — a useful way to tell
    /// this failure apart from an actual save failure.
    /// </para>
    /// </summary>
    private async Task EvictSiteCacheAsync(CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is { } tenantId)
        {
            await cache.EvictByTagAsync(TenantSiteCachePolicy.TagFor(tenantId), cancellationToken);
        }
    }

    /// <summary>Everything the owner has, available or not, in the order the shop shows them.</summary>
    public Task<List<Product>> ListAsync(CancellationToken cancellationToken = default) =>
        db.Products
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public async Task<Product> AddAsync(string name, CancellationToken cancellationToken = default)
    {
        var product = new Product
        {
            Name = string.IsNullOrWhiteSpace(name) ? "New item" : name.Trim(),
            SortOrder = await NextSortOrderAsync(cancellationToken),
        };

        product.Slug = await UniqueSlugAsync(product.Name, product.Id, cancellationToken);

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        await EvictSiteCacheAsync(cancellationToken);

        return product;
    }

    /// <summary>
    /// Saves an edit.
    /// <para>
    /// The address follows the name. It has to: a product added from the button starts as
    /// "New item", so keeping the original slug left everything the owner ever renamed sitting at
    /// <c>/products/new-item-2</c>. The site's own links are always built from the current slug, so
    /// nothing internal breaks; the cost is that a link shared before a rename stops working, which
    /// is the better of the two bad outcomes while a catalog is being set up.
    /// </para>
    /// </summary>
    public async Task SaveAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        product.Name = string.IsNullOrWhiteSpace(product.Name) ? "New item" : product.Name.Trim();
        product.Slug = await UniqueSlugAsync(product.Name, product.Id, cancellationToken);
        product.Currency = string.IsNullOrWhiteSpace(product.Currency) ? "GHS" : product.Currency.Trim().ToUpperInvariant();
        product.UpdatedUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await EvictSiteCacheAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is not null)
        {
            db.Products.Remove(product);
            await db.SaveChangesAsync(cancellationToken);
            await EvictSiteCacheAsync(cancellationToken);
        }
    }

    /// <summary>Renumbers the whole list from the order given, so one drag does not need N updates.</summary>
    public async Task ReorderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default)
    {
        var products = await db.Products.ToListAsync(cancellationToken);

        for (var index = 0; index < orderedIds.Count; index++)
        {
            var product = products.FirstOrDefault(p => p.Id == orderedIds[index]);
            if (product is not null)
            {
                product.SortOrder = index;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await EvictSiteCacheAsync(cancellationToken);
    }

    private async Task<int> NextSortOrderAsync(CancellationToken cancellationToken)
    {
        var any = await db.Products.AnyAsync(cancellationToken);

        return any ? await db.Products.MaxAsync(p => p.SortOrder, cancellationToken) + 1 : 0;
    }

    /// <summary>
    /// A slug that is free within this tenant, trying "jollof", "jollof-2", "jollof-3". Falls back
    /// to the id when a name normalises to nothing — a product called "!!!" still needs an address.
    /// </summary>
    private async Task<string> UniqueSlugAsync(string? from, Guid productId, CancellationToken cancellationToken)
    {
        var basis = Slug.From(from, MaxSlugLength);

        if (basis.Length == 0)
        {
            basis = $"item-{productId:N}"[..12];
        }

        var taken = await db.Products
            .Where(p => p.Id != productId && p.Slug.StartsWith(basis))
            .Select(p => p.Slug)
            .ToListAsync(cancellationToken);

        if (!taken.Contains(basis, StringComparer.OrdinalIgnoreCase))
        {
            return basis;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{basis}-{suffix}";

            if (!taken.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }
    }
}
