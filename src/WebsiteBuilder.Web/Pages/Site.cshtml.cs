using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Caching;
using WebsiteBuilder.Web.Leads;
using WebsiteBuilder.Web.Shop;

namespace WebsiteBuilder.Web.Pages;

/// <summary>
/// Serves a tenant's published site. Only the published snapshot is ever read here — a draft
/// must never be reachable by a visitor. The contact form posts back to this same page (WB-31);
/// the tenant is already resolved from the host by the tenant-resolution middleware.
/// </summary>
// The enquiry form is a public, unauthenticated post from an anonymous visitor, so there is no
// session to protect with an antiforgery token (and the page is output-cached, which a per-user
// token would fight). A hidden honeypot field guards against the crudest bots instead.
[IgnoreAntiforgeryToken]
[OutputCache(PolicyName = TenantSiteCachePolicy.Name)]
public class SiteModel(WebsiteBuilderDbContext db, LeadsService leads, ShopCatalog catalog) : PageModel
{
    public SiteDefinition? Definition { get; private set; }

    /// <summary>Live catalog rows for the shop section, if the page has one. Empty otherwise.</summary>
    public IReadOnlyList<Product> Products { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await LoadDefinitionAsync();

        if (Definition is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await LoadProductsAsync();
    }

    public async Task OnPostAsync(string? name, string? phoneNumber, string? email, string? message, string? website)
    {
        await LoadDefinitionAsync();

        if (Definition is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // The honeypot is invisible to people; a filled value is a bot. Show success and drop it.
        if (!string.IsNullOrWhiteSpace(website))
        {
            ViewData["EnquirySent"] = true;
            return;
        }

        var siteId = await db.Sites
            .AsNoTracking()
            .Where(s => s.Published != null)
            .OrderBy(s => s.CreatedUtc)
            .Select(s => s.Id)
            .FirstOrDefaultAsync();

        if (siteId != Guid.Empty && await leads.CaptureAsync(siteId, name, phoneNumber, email, message))
        {
            ViewData["EnquirySent"] = true;
        }
        else
        {
            ViewData["EnquiryError"] = "Please add your name, a message, and a phone or email so we can reply.";
            ViewData["EnquiryName"] = name;
            ViewData["EnquiryPhone"] = phoneNumber;
            ViewData["EnquiryEmail"] = email;
            ViewData["EnquiryMessage"] = message;
        }
    }

    /// <summary>
    /// Only when the page actually has a shop. A site without one must not pay for a query, and
    /// most of them will never have one.
    /// </summary>
    private async Task LoadProductsAsync()
    {
        var shop = Definition!.Sections.OfType<ShopSection>().FirstOrDefault(s => s.Visible);

        if (shop is not null)
        {
            Products = await catalog.ForVisitorsAsync(shop.MaxItems, HttpContext.RequestAborted);
        }
    }

    private async Task LoadDefinitionAsync()
    {
        // The tenant query filter restricts this to the resolved tenant's own rows.
        Definition = await db.Sites
            .AsNoTracking()
            .Where(s => s.Published != null)
            .OrderBy(s => s.CreatedUtc)
            .Select(s => s.Published)
            .FirstOrDefaultAsync();
    }
}
