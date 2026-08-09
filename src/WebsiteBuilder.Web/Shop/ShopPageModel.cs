using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Pages;

namespace WebsiteBuilder.Web.Shop;

/// <summary>
/// Shared behaviour for the shop pages a visitor sees on a tenant host.
/// <para>
/// Each of them needs the published site for its chrome — the same header, footer and call bar as
/// the home page — and each must refuse to render for a tenant that has no published site or no
/// shop section. Getting that wrong would leave a shop reachable at a URL for a business that
/// never opened one.
/// </para>
/// <para>
/// These pages are deliberately <b>not output-cached</b>: the cart is per-visitor and the catalog
/// is live. The home page can be cached because it is the same for everybody.
/// </para>
/// </summary>
public abstract class ShopPageModel(WebsiteBuilderDbContext db) : PageModel
{
    public SiteChrome Chrome { get; private set; } = null!;

    /// <summary>
    /// Loads the published site and confirms it has a shop. Returns null when the page may render.
    /// </summary>
    protected async Task<IActionResult?> LoadChromeAsync()
    {
        // The tenant query filter restricts this to the resolved tenant's own rows.
        var definition = await db.Sites
            .AsNoTracking()
            .Where(s => s.Published != null)
            .OrderBy(s => s.CreatedUtc)
            .Select(s => s.Published)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (definition is null)
        {
            return NotFound();
        }

        Chrome = new SiteChrome(definition);

        // No shop section means this business does not sell online, and its /shop URL should be
        // as absent as any other page we do not serve.
        return Chrome.HasShop ? null : NotFound();
    }
}
