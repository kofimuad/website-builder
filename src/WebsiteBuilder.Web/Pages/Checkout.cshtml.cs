using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Shop;

namespace WebsiteBuilder.Web.Pages;

/// <summary>
/// Where an order is actually placed: the customer's details, and the button that commits it.
/// <para>
/// Ecommerce v1 went straight from the cart to a WhatsApp link, which meant the order only existed
/// if the customer pressed send in WhatsApp and the owner had no record either way. This page is
/// the missing step. It writes an <c>Order</c> first and emails the owner, and only then offers
/// WhatsApp — so the sale survives a customer who never sends the message.
/// </para>
/// </summary>
// An anonymous visitor with no session, exactly like the enquiry form and the add-to-cart post.
// There is no authority to forge: the worst a cross-site post can do is place an order that the
// owner then fails to contact anybody about, and every price is read from the database.
[IgnoreAntiforgeryToken]
public class CheckoutModel(WebsiteBuilderDbContext db, ShopCatalog catalog, OrdersService orders)
    : ShopPageModel(db)
{
    public IReadOnlyList<PricedLine> Lines { get; private set; } = [];

    public long? TotalMinor { get; private set; }

    public string? Currency { get; private set; }

    /// <summary>What the customer typed, kept so a rejected form re-renders rather than emptying.</summary>
    public string? CustomerName { get; private set; }

    public string? PhoneNumber { get; private set; }

    public string? Email { get; private set; }

    public string? Note { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() is { } refusal)
        {
            return refusal;
        }

        // Nothing to check out. Sending them to the cart says so in the place they expect it.
        return Lines.Count == 0 ? Redirect("/cart") : Page();
    }

    public async Task<IActionResult> OnPostAsync(
        string? customerName, string? phoneNumber, string? email, string? note, string? website)
    {
        if (await LoadAsync() is { } refusal)
        {
            return refusal;
        }

        CustomerName = customerName;
        PhoneNumber = phoneNumber;
        Email = email;
        Note = note;

        // The same honeypot the enquiry form uses. Invisible to people; a filled value is a bot.
        // Show the cart rather than an error, and store nothing.
        if (!string.IsNullOrWhiteSpace(website))
        {
            return Redirect("/cart");
        }

        var siteId = await db.Sites
            .AsNoTracking()
            .Where(s => s.Published != null)
            .OrderBy(s => s.CreatedUtc)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (siteId == Guid.Empty)
        {
            return NotFound();
        }

        var result = await orders.PlaceAsync(
            siteId, Lines, customerName, phoneNumber, email, note, HttpContext.RequestAborted);

        if (!result.Placed)
        {
            Error = result.Failure switch
            {
                OrderFailure.EmptyCart => "Your order is empty. Add something to it first.",
                _ => "Please add your name and a phone number or email, so we can confirm your order.",
            };

            return result.Failure == OrderFailure.EmptyCart ? Redirect("/cart") : Page();
        }

        // The basket has become an order, so it stops being a basket. Leaving it filled is how a
        // customer accidentally orders the same thing twice.
        CartCookie.Write(HttpContext, new Cart());

        // Redirect after post: a refresh on the confirmation page must not place a second order.
        return Redirect($"/order/{result.Order!.Reference}");
    }

    private async Task<IActionResult?> LoadAsync()
    {
        if (await LoadChromeAsync() is { } refusal)
        {
            return refusal;
        }

        var cart = CartCookie.Read(HttpContext);
        var products = await catalog.ByIdsAsync(cart.ProductIds.ToList(), HttpContext.RequestAborted);

        // A product withdrawn since it went in the basket is dropped, and the cookie rewritten —
        // the same rule as the cart page, and it matters more here: this is the last point before
        // the prices are copied into a permanent record.
        cart.KeepOnly(products.Keys.ToList());
        CartCookie.Write(HttpContext, cart);

        Lines = cart.Lines
            .Select(line => new PricedLine(products[line.ProductId], line.Quantity))
            .OrderBy(l => l.Product.SortOrder)
            .ThenBy(l => l.Product.Name)
            .ToList();

        (TotalMinor, Currency) = OrderMessage.Total(Lines);

        return null;
    }
}
