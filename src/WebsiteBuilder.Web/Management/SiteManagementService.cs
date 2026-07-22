using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Data;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Web.Publishing;

namespace WebsiteBuilder.Web.Management;

public sealed record ManagedSite(Site Site, BusinessProfile Profile);

/// <summary>
/// Backs the owner's management pages. A site is addressed by id; loading it puts its tenant into
/// scope, after which the normal tenant-filtered queries and publishing work. Until sign-in (WB-15)
/// the id is the only gate; afterwards this must verify the site belongs to the signed-in owner.
/// </summary>
public sealed class SiteManagementService(
    WebsiteBuilderDbContext db,
    TenantContext tenantContext,
    SitePublisher publisher)
{
    /// <summary>Loads a site and its profile by id and scopes the context to that tenant. Null if not found.</summary>
    public async Task<ManagedSite?> LoadAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        var site = await db.Sites
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == siteId, cancellationToken);

        if (site is null)
        {
            return null;
        }

        // Act as this tenant for the rest of the unit of work.
        tenantContext.TenantId = site.TenantId;

        var profile = await db.BusinessProfiles.FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return null;
        }

        return new ManagedSite(site, profile);
    }

    /// <summary>
    /// Saves edited profile details and flows the contact-type fields into the site's draft. The
    /// published site is untouched until the owner republishes. Requires the tenant to be in scope
    /// (call <see cref="LoadAsync"/> first).
    /// </summary>
    public async Task SaveProfileAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, cancellationToken)
            ?? throw new InvalidOperationException($"Site {siteId} is not in scope for the current tenant.");
        var profile = await db.BusinessProfiles.FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No business profile is in scope for the current tenant.");

        profile.UpdatedUtc = DateTimeOffset.UtcNow;
        site.Name = profile.BusinessName;
        ProfileToDraft.Apply(profile, site.Draft);

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task PublishAsync(Guid siteId, CancellationToken cancellationToken = default) =>
        publisher.PublishAsync(siteId, cancellationToken);
}
