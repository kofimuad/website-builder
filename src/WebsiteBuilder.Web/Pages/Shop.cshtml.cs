using Microsoft.AspNetCore.Mvc;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Shop;

namespace WebsiteBuilder.Web.Pages;

/// <summary>The full catalog on a tenant host.</summary>
public class ShopModel(WebsiteBuilderDbContext db, ShopCatalog catalog) : ShopPageModel(db)
{
    public IReadOnlyList<Product> Products { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadChromeAsync() is { } refusal)
        {
            return refusal;
        }

        Products = await catalog.ForVisitorsAsync(cancellationToken: HttpContext.RequestAborted);

        return Page();
    }
}
