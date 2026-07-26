using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Auth;

namespace WebsiteBuilder.Web.Pages;

/// <summary>
/// Renders a site's <em>draft</em> so the owner can see it before publishing. Runs on the platform
/// host (not a tenant subdomain), so it looks the site up across the tenant filter and joins to the
/// signed-in owner to authorise it. A draft is unpublished work — pricing not yet announced, copy
/// not yet approved — so the id alone must not be enough to read one.
///
/// This is a Razor Page rather than a component, so unlike the management pages it has a real
/// HttpContext and reads identity straight off <c>User</c>.
/// </summary>
[Authorize]
public class SitePreviewModel(WebsiteBuilderDbContext db) : PageModel
{
    public SiteDefinition? Draft { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid siteId)
    {
        var ownerId = User.OwnerId();

        if (ownerId is null)
        {
            return NotFound();
        }

        Draft = await db.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.Id == siteId)
            .Join(
                db.Tenants.Where(t => t.OwnerId == ownerId),
                s => s.TenantId,
                t => t.Id,
                (s, _) => s.Draft)
            .FirstOrDefaultAsync();

        // Not-yours and not-found are the same answer, so this cannot confirm a site id exists.
        if (Draft is null)
        {
            return NotFound();
        }

        return Page();
    }
}
