namespace WebsiteBuilder.Web.Shop;

/// <summary>Reads and writes the cart cookie on a request.</summary>
public static class CartCookie
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    public static Cart Read(HttpContext context) =>
        Cart.Parse(context.Request.Cookies[Cart.CookieName]);

    public static void Write(HttpContext context, Cart cart)
    {
        if (cart.IsEmpty)
        {
            context.Response.Cookies.Delete(Cart.CookieName);
            return;
        }

        context.Response.Cookies.Append(Cart.CookieName, cart.ToString(), new CookieOptions
        {
            // No JavaScript reads this, so nothing needs to.
            HttpOnly = true,
            // Lax rather than Strict: an order link shared into WhatsApp and opened from there is
            // a top-level cross-site navigation, and losing the basket on arrival would be absurd.
            SameSite = SameSiteMode.Lax,
            // Tenant sites are HTTPS-only in production; leaving this on Always in development
            // would silently drop every cart over plain-HTTP localhost.
            Secure = context.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.Add(Lifetime),
            IsEssential = true,
            Path = "/",
        });
    }
}
