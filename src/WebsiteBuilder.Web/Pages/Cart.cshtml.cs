using Microsoft.AspNetCore.Mvc;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Shop;

namespace WebsiteBuilder.Web.Pages;

/// <summary>
/// The order a visitor has built up, and the button that sends it to the owner on WhatsApp.
/// </summary>
// Anonymous visitor, no session, nothing of value in the cookie — same reasoning as the enquiry
// form and the add-to-cart post.
[IgnoreAntiforgeryToken]
public class CartModel(WebsiteBuilderDbContext db, ShopCatalog catalog) : ShopPageModel(db)
{
    public IReadOnlyList<PricedLine> Lines { get; private set; } = [];

    public long? TotalMinor { get; private set; }

    public string? Currency { get; private set; }

    /// <summary>The wa.me link with the order pre-typed, or null when there is no number to send to.</summary>
    public string? OrderLink { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() is { } refusal)
        {
            return refusal;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid productId, int quantity)
    {
        if (await LoadChromeAsync() is { } refusal)
        {
            return refusal;
        }

        var cart = CartCookie.Read(HttpContext);
        cart.Set(productId, quantity);
        CartCookie.Write(HttpContext, cart);

        return Redirect("/cart");
    }

    private async Task<IActionResult?> LoadAsync()
    {
        if (await LoadChromeAsync() is { } refusal)
        {
            return refusal;
        }

        var cart = CartCookie.Read(HttpContext);
        var products = await catalog.ByIdsAsync(cart.ProductIds.ToList(), HttpContext.RequestAborted);

        // A product deleted or marked unavailable since it went in the basket is dropped, and the
        // cookie is rewritten so it stays dropped. Showing a line nobody can be sold is worse than
        // quietly losing it.
        cart.KeepOnly(products.Keys.ToList());
        CartCookie.Write(HttpContext, cart);

        // Prices come from these rows, never from the cookie — the cookie only says what and how
        // many. That is what makes an unsigned cart safe.
        Lines = cart.Lines
            .Select(line => new PricedLine(products[line.ProductId], line.Quantity))
            .OrderBy(l => l.Product.SortOrder)
            .ThenBy(l => l.Product.Name)
            .ToList();

        (TotalMinor, Currency) = OrderMessage.Total(Lines);

        if (Lines.Count > 0 && !string.IsNullOrWhiteSpace(Chrome.Contact?.WhatsAppNumber))
        {
            OrderLink = OrderMessage.Link(
                Chrome.Contact.WhatsAppNumber,
                OrderMessage.Compose(Chrome.BusinessName, Lines));
        }

        return null;
    }
}
