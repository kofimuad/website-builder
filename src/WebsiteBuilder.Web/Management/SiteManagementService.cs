using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Data;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Web.Publishing;

namespace WebsiteBuilder.Web.Management;

public sealed record ManagedSite(Site Site, BusinessProfile Profile, string Subdomain);

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
    SitePublisher publisher,
    IOptions<TenantResolutionOptions> tenantOptions)
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
        // The site is projected inside an anonymous type rather than selected alone so the tenant's
        // address comes back in the same round trip. It stays tracked either way, which the editor
        // relies on when it mutates the draft in place.
        var found = await db.Sites
            .IgnoreQueryFilters()
            .Where(s => s.Id == siteId)
            .Join(
                db.Tenants.Where(t => t.OwnerId == ownerId),
                s => s.TenantId,
                t => t.Id,
                (s, t) => new { Site = s, t.Subdomain })
            .FirstOrDefaultAsync(cancellationToken);

        if (found is null)
        {
            return null;
        }

        // Only now: act as this tenant for the rest of the unit of work.
        tenantContext.TenantId = found.Site.TenantId;

        var profile = await db.BusinessProfiles.FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return null;
        }

        return new ManagedSite(found.Site, profile, found.Subdomain);
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

    /// <summary>
    /// Checks an address the owner typed: shape first, then whether anyone holds it.
    /// <see cref="SubdomainProblem.None"/> means it looked free a moment ago — it is not a
    /// reservation, so <see cref="ChangeSubdomainAsync"/> checks again and can still refuse.
    /// </summary>
    public async Task<SubdomainProblem> CheckSubdomainAsync(
        string? candidate,
        CancellationToken cancellationToken = default)
    {
        var problem = SubdomainPolicy.Validate(candidate, tenantOptions.Value);
        if (problem != SubdomainProblem.None)
        {
            return problem;
        }

        var value = SubdomainPolicy.Normalize(candidate);

        // Across all tenants, not just the one in scope: the address has to be unique platform-wide.
        var taken = await db.Tenants.AsNoTracking()
            .AnyAsync(t => t.Subdomain == value, cancellationToken);

        return taken ? SubdomainProblem.Taken : SubdomainProblem.None;
    }

    /// <summary>
    /// Moves the loaded site's tenant to a new address. Only allowed while the site has never been
    /// published: once a link is out in the world — printed on a card, sent on WhatsApp — changing
    /// where it points breaks it silently, and nothing redirects the old one. Renaming a live site
    /// is its own story with its own answer for the old address.
    /// </summary>
    public async Task<SubdomainProblem> ChangeSubdomainAsync(
        Guid siteId,
        string? candidate,
        CancellationToken cancellationToken = default)
    {
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, cancellationToken)
            ?? throw new InvalidOperationException($"Site {siteId} is not in scope for the current tenant.");

        if (site.IsPublished)
        {
            throw new InvalidOperationException(
                "The address of a published site cannot be changed — existing links would break.");
        }

        var problem = await CheckSubdomainAsync(candidate, cancellationToken);
        if (problem != SubdomainProblem.None)
        {
            return problem;
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == site.TenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {site.TenantId} is missing.");

        var previous = tenant.Subdomain;
        tenant.Subdomain = SubdomainPolicy.Normalize(candidate);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Two owners can pass the availability check in the same instant. The unique index is
            // what actually decides, so report the loser the same "taken" the check would have.
            tenant.Subdomain = previous;
            return SubdomainProblem.Taken;
        }

        return SubdomainProblem.None;
    }
}
