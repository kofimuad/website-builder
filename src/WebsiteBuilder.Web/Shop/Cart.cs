using System.Globalization;

namespace WebsiteBuilder.Web.Shop;

/// <summary>One line of a cart: what, and how many. Never what it costs.</summary>
public readonly record struct CartLine(Guid ProductId, int Quantity);

/// <summary>
/// A visitor's cart, parsed from and written to a cookie.
/// <para>
/// A cookie rather than a server session because a tenant site is output-cached and otherwise
/// entirely stateless, and because a shopper on a phone should not lose their basket to an app
/// restart. It is unsigned on purpose: <b>the cart carries no authority</b>. It holds product ids
/// and quantities, and every price, name and availability check is read from the database at
/// render time. The worst a forged cookie can do is put a product someone already has access to
/// into their own basket.
/// </para>
/// <para>
/// Cookies are scoped to a host by the browser, so one tenant's cart can never be read on another
/// tenant's site.
/// </para>
/// </summary>
public sealed class Cart
{
    public const string CookieName = "csbuild_cart";

    /// <summary>Caps, so a hand-written cookie cannot make the cart page do unbounded work.</summary>
    public const int MaxLines = 40;
    public const int MaxQuantity = 99;

    private readonly Dictionary<Guid, int> _lines = [];

    public IReadOnlyCollection<Guid> ProductIds => _lines.Keys;

    public bool IsEmpty => _lines.Count == 0;

    public int TotalItems => _lines.Values.Sum();

    public IEnumerable<CartLine> Lines => _lines.Select(pair => new CartLine(pair.Key, pair.Value));

    public int QuantityOf(Guid productId) => _lines.GetValueOrDefault(productId);

    /// <summary>
    /// Reads a cookie of the form <c>id:qty,id:qty</c>. Anything malformed is skipped rather than
    /// throwing: the value came from a browser, and a corrupt cart must not be an error page
    /// between a customer and their order.
    /// </summary>
    public static Cart Parse(string? cookie)
    {
        var cart = new Cart();

        if (string.IsNullOrWhiteSpace(cookie))
        {
            return cart;
        }

        foreach (var entry in cookie.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            if (!Guid.TryParse(entry[..separator], out var id))
            {
                continue;
            }

            if (!int.TryParse(entry[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var quantity))
            {
                continue;
            }

            cart.Set(id, quantity);

            if (cart._lines.Count >= MaxLines)
            {
                break;
            }
        }

        return cart;
    }

    /// <summary>Sets a line's quantity; zero or less removes it. Quantities are clamped, not rejected.</summary>
    public void Set(Guid productId, int quantity)
    {
        if (quantity <= 0)
        {
            _lines.Remove(productId);
            return;
        }

        if (_lines.Count >= MaxLines && !_lines.ContainsKey(productId))
        {
            return;
        }

        _lines[productId] = Math.Min(quantity, MaxQuantity);
    }

    public void Add(Guid productId, int quantity) => Set(productId, QuantityOf(productId) + quantity);

    public void Remove(Guid productId) => _lines.Remove(productId);

    /// <summary>Drops lines whose product no longer exists or has been made unavailable.</summary>
    public void KeepOnly(IReadOnlyCollection<Guid> availableIds)
    {
        foreach (var id in _lines.Keys.Where(id => !availableIds.Contains(id)).ToList())
        {
            _lines.Remove(id);
        }
    }

    public override string ToString() =>
        string.Join(',', _lines.Select(pair => $"{pair.Key:N}:{pair.Value}"));
}
