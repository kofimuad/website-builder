using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Data;

namespace WebsiteBuilder.Web.Pages;

/// <summary>
/// Renders a site's <em>draft</em> so the owner can see it before publishing. Runs on the platform
/// host (not a tenant subdomain), so it looks up the site by id across the tenant filter — the id
/// is the capability. Once sign-in exists (WB-15) this must also check the site belongs to the
/// signed-in owner.
/// </summary>
public class SitePreviewModel(WebsiteBuilderDbContext db) : PageModel
{
    public SiteDefinition? Draft { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid siteId)
    {
        Draft = await db.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.Id == siteId)
            .Select(s => s.Draft)
            .FirstOrDefaultAsync();

        if (Draft is null)
        {
            return NotFound();
        }

        return Page();
    }
}
