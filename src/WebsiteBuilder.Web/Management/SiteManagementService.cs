using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Data;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Web.Publishing;

namespace WebsiteBuilder.Web.Management;

public sealed record ManagedSite(Site Site, BusinessProfile Profile);

/// <summary>One row of the owner's dashboard: a site plus the few facts the list needs.</summary>
public sealed record OwnedSite(
    Guid SiteId,
    Guid TenantId,
    string Name,
    string Subdomain,
    bool IsPublished,
    DateTimeOffset? PublishedUtc,
    DateTimeOffset CreatedUtc,
    int TotalLeads,
    int UnreadLeads);

/// <summary>
/// Backs the owner's management pages. A site is addressed by id, and loading it puts that site's
/// tenant into scope — so the ownership check has to happen *before* the scope is granted, not
/// after. Everything downstream trusts <see cref="TenantContext"/>, which makes
/// <see cref="LoadAsync"/> the single gate protecting every site in the database.
///
/// The owner id is passed in rather than read from ambient state: inside a Blazor circuit there is
/// no <c>HttpContext</c> to read it from, and a gate that silently sees "no user" would fail open.
/// </summary>
public sealed class SiteManagementService(
    WebsiteBuilderDbContext db,
    TenantContext tenantContext,
    SitePublisher publisher)
{
    /// <summary>
    /// Loads a site and its profile for the given owner and scopes the context to that tenant.
    /// Returns null when the site does not exist *or* is not theirs — the two are deliberately
    /// indistinguishable, so this cannot be used to probe which site ids are real.
    /// </summary>
    public async Task<ManagedSite?> LoadAsync(
        Guid siteId,
        Guid? ownerId,
        CancellationToken cancellationToken = default)
    {
        if (ownerId is null)
        {
            return null;
        }

        // IgnoreQueryFilters because no tenant is in scope yet — that is what this call establishes.
        // The join to Tenants is the authorisation: a site whose tenant has a different owner, or
        // no owner at all, never gets past here.
        var site = await db.Sites
            .IgnoreQueryFilters()
            .Where(s => s.Id == siteId)
            .Join(
                db.Tenants.Where(t => t.OwnerId == ownerId),
                s => s.TenantId,
                t => t.Id,
                (s, _) => s)
            .FirstOrDefaultAsync(cancellationToken);

        if (site is null)
        {
            return null;
        }

        // Only now: act as this tenant for the rest of the unit of work.
        tenantContext.TenantId = site.TenantId;

        var profile = await db.BusinessProfiles.FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return null;
        }

        return new ManagedSite(site, profile);
    }

    /// <summary>
    /// Every site the owner has, newest first, with its tenant subdomain and unread lead count.
    /// Reads across tenants by design — this is the one query that legitimately spans them — so it
    /// filters on owner id directly instead of relying on tenant scope.
    /// </summary>
    public async Task<List<OwnedSite>> ListForOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var sites = await (
                from tenant in db.Tenants.AsNoTracking().Where(t => t.OwnerId == ownerId)
                join site in db.Sites.AsNoTracking().IgnoreQueryFilters()
                    on tenant.Id equals site.TenantId
                orderby site.CreatedUtc descending
                select new
                {
                    SiteId = site.Id,
                    TenantId = tenant.Id,
                    site.Name,
                    tenant.Subdomain,
                    IsPublished = site.Published != null,
                    site.PublishedUtc,
                    site.CreatedUtc,
                })
            .ToListAsync(cancellationToken);

        var siteIds = sites.Select(s => s.SiteId).ToList();

        // Counted in a second grouped query rather than as subqueries in the projection above:
        // a correlated count per row does not translate once the filter is ignored, and this is
        // one round trip either way.
        var leadCounts = await db.Leads
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(l => siteIds.Contains(l.SiteId))
            .GroupBy(l => l.SiteId)
            .Select(g => new
            {
                SiteId = g.Key,
                Total = g.Count(),
                Unread = g.Count(l => !l.IsRead),
            })
            .ToDictionaryAsync(x => x.SiteId, cancellationToken);

        return sites
            .Select(s => new OwnedSite(
                s.SiteId,
                s.TenantId,
                s.Name,
                s.Subdomain,
                s.IsPublished,
                s.PublishedUtc,
                s.CreatedUtc,
                leadCounts.TryGetValue(s.SiteId, out var c) ? c.Total : 0,
                leadCounts.TryGetValue(s.SiteId, out var u) ? u.Unread : 0))
            .ToList();
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

    /// <summary>
    /// Persists in-place edits to the loaded site's draft. The editor mutates the tracked draft
    /// directly, so this just commits; the tenant must already be in scope (via <see cref="LoadAsync"/>).
    /// </summary>
    public Task SaveDraftAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public Task PublishAsync(Guid siteId, CancellationToken cancellationToken = default) =>
        publisher.PublishAsync(siteId, cancellationToken);
}
