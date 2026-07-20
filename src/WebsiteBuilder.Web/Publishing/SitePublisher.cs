using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Caching;

namespace WebsiteBuilder.Web.Publishing;

/// <summary>
/// Promotes a site's draft to live. Publishing and cache eviction belong together: a published
/// site that visitors still see the old copy of has not really been published.
/// </summary>
public sealed class SitePublisher(WebsiteBuilderDbContext db, IOutputCacheStore cache)
{
    public async Task PublishAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, cancellationToken)
            ?? throw new InvalidOperationException($"Site {siteId} was not found for the current tenant.");

        site.Publish();
        await db.SaveChangesAsync(cancellationToken);

        // After the commit: evicting first would leave a window where a request could re-cache
        // the old content.
        await cache.EvictByTagAsync(TenantSiteCachePolicy.TagFor(site.TenantId), cancellationToken);
    }
}
