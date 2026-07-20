using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Caching;

namespace WebsiteBuilder.Web.Pages;

/// <summary>
/// Serves a tenant's published site. Only the published snapshot is ever read here — a draft
/// must never be reachable by a visitor.
/// </summary>
[OutputCache(PolicyName = TenantSiteCachePolicy.Name)]
public class SiteModel(WebsiteBuilderDbContext db) : PageModel
{
    public SiteDefinition? Definition { get; private set; }

    public async Task OnGetAsync()
    {
        // The tenant query filter restricts this to the resolved tenant's own rows.
        Definition = await db.Sites
            .AsNoTracking()
            .Where(s => s.Published != null)
            .OrderBy(s => s.CreatedUtc)
            .Select(s => s.Published)
            .FirstOrDefaultAsync();

        if (Definition is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }
}
