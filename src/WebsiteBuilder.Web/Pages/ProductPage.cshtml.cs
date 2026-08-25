using Microsoft.AspNetCore.Mvc;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Shop;

namespace WebsiteBuilder.Web.Pages;

/// <summary>
/// One product on a tenant host, and the place a cart line is created.
/// </summary>
// The add-to-cart post is an anonymous visitor action with no session to protect, exactly like the
// enquiry form. Nothing it can do is worth a token: the cart carries no authority and every price
// is read from the database.
[IgnoreAntiforgeryToken]
public class ProductPageModel(WebsiteBuilderDbContext db, ShopCatalog catalog) : ShopPageModel(db)
{
    public Product Product { get; private set; } = null!;

    public bool JustAdded { get; private set; }

    public int CartCount { get; private set; }

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        if (await LoadAsync(slug) is { } refusal)
        {
            return refusal;
        }

        JustAdded = Request.Query.ContainsKey("added");

        return Page();
    }

    /// <summary>
    /// Adds this product to the cart, and — when the customer used "Order now" — takes them
    /// straight to the checkout.
    /// <para>
    /// The direct route exists because ordering one thing was the common case and the cart was in
    /// the way of it: a customer who wanted a single item had to add it, find the cart, and only
    /// then discover how to order. Both buttons go through the same cart so the two paths cannot
    /// drift apart, and so an item added on the way to checkout is still there if the customer
    /// backs out and carries on shopping.
    /// </para>
    /// </summary>
    public async Task<IActionResult> OnPostAsync(string slug, int quantity = 1, string? orderNow = null)
    {
        if (await LoadAsync(slug) is { } refusal)
        {
            return refusal;
        }

        var cart = CartCookie.Read(HttpContext);
        cart.Add(Product.Id, quantity <= 0 ? 1 : quantity);
        CartCookie.Write(HttpContext, cart);

        // Redirect after post so a refresh does not add the item again.
        return Redirect(orderNow is not null ? "/checkout" : $"/products/{Product.Slug}?added=1");
    }

    private async Task<IActionResult?> LoadAsync(string slug)
    {
        if (await LoadChromeAsync() is { } refusal)
        {
            return refusal;
        }

        var product = await catalog.BySlugAsync(slug, HttpContext.RequestAborted);

        if (product is null)
        {
            return NotFound();
        }

        Product = product;
        CartCount = CartCookie.Read(HttpContext).TotalItems;

        return null;
    }
}
