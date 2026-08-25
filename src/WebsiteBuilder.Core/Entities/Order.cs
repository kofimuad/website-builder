using System.Security.Cryptography;
using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Core.Entities;

/// <summary>Where an order has got to. The owner moves it along by hand — there is no payment to do it for them.</summary>
public enum OrderStatus
{
    /// <summary>Placed by a customer, not yet looked at.</summary>
    New,

    /// <summary>The owner has spoken to the customer and the order is going ahead.</summary>
    Confirmed,

    /// <summary>Delivered or collected, and paid for.</summary>
    Fulfilled,

    /// <summary>Called off, by either side.</summary>
    Cancelled,
}

/// <summary>
/// An order a visitor placed on a published site.
/// <para>
/// Ecommerce v1 sent the order as a WhatsApp message and stored nothing, which meant an order only
/// existed if the customer actually pressed send in WhatsApp, and the owner had no record of it
/// afterwards. This is that missing half: the order is <b>committed to the database first</b>, and
/// the WhatsApp message becomes a convenience on top rather than the only copy.
/// </para>
/// <para>
/// Modelled on <see cref="Lead"/> deliberately — tenant-owned, captured from an anonymous visitor,
/// emailed to the owner, and read back in an inbox that marks it read. An order is a lead that
/// happens to name products.
/// </para>
/// </summary>
public class Order : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }

    /// <summary>
    /// The short code the customer quotes and the owner searches for — "K4P2QX". Unique per tenant.
    /// <para>
    /// It exists because the two of them will finish this conversation on WhatsApp or on the phone,
    /// where a Guid is unusable. See <see cref="NewReference"/> for why it is drawn from a reduced
    /// alphabet.
    /// </para>
    /// </summary>
    public string Reference { get; set; } = "";

    public string CustomerName { get; set; } = "";
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }

    /// <summary>Where to deliver, when to collect, anything else the customer wants to say.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// The order's total at the moment it was placed, or null when the lines could not be totalled
    /// — nothing priced, or priced in more than one currency. A wrong total is worse than none.
    /// </summary>
    public long? TotalMinor { get; set; }

    public string? Currency { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.New;

    /// <summary>Cleared when the owner opens the orders inbox; drives the "new" count, as on leads.</summary>
    public bool IsRead { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<OrderItem> Items { get; set; } = [];

    /// <summary>
    /// A six-character reference from an alphabet with no 0/O, 1/I/L or 5/S in it.
    /// <para>
    /// The whole point of this code is that it survives being read down a phone line or typed into
    /// a chat, so the characters people reliably confuse are simply not in it. Drawn from a
    /// cryptographic RNG, not <see cref="Random"/>: two orders placed in the same tick must not be
    /// able to collide, and a guessable reference would let one customer ask after another's order.
    /// </para>
    /// </summary>
    public static string NewReference()
    {
        const string alphabet = "ABCDEFGHJKMNPQRTUVWXYZ2346789";

        return string.Create(6, alphabet, static (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
            }
        });
    }
}

/// <summary>
/// One line of an order, with the product's name and price <b>copied in at the time of ordering</b>.
/// <para>
/// This is the reason an order is not just a list of product ids. Products are live rows with no
/// draft copy — the owner can raise a price or rename an item minutes after the order arrives — and
/// an order that silently re-priced itself when they did would be a record of nothing. The snapshot
/// is what makes it a record. <see cref="ProductId"/> is kept alongside it purely so the owner can
/// click through to the item, and goes null rather than taking the order with it if the product is
/// ever deleted.
/// </para>
/// </summary>
public class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }

    /// <summary>The product ordered, or null once that product has been deleted.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>The name as the customer saw it.</summary>
    public string ProductName { get; set; } = "";

    /// <summary>The price per unit as the customer saw it, or null for an "ask for a price" item.</summary>
    public long? UnitPriceMinor { get; set; }

    public string Currency { get; set; } = "GHS";

    public int Quantity { get; set; } = 1;

    public long? LineTotalMinor => UnitPriceMinor * Quantity;
}
