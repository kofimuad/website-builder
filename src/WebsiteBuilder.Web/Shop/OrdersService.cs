using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Data;

namespace WebsiteBuilder.Web.Shop;

/// <summary>The reason an order could not be placed, and the words the checkout page shows for it.</summary>
public enum OrderFailure
{
    None,

    /// <summary>Nothing left to order — the basket emptied out, usually because a product was withdrawn.</summary>
    EmptyCart,

    /// <summary>No name, or no way at all to reply. An order nobody can be contacted about is a dead end.</summary>
    MissingDetails,
}

/// <summary>The outcome of placing an order: the order itself, or why it did not happen.</summary>
public readonly record struct OrderResult(Order? Order, OrderFailure Failure)
{
    public bool Placed => Order is not null;

    public static OrderResult Success(Order order) => new(order, OrderFailure.None);

    public static OrderResult Failed(OrderFailure failure) => new(null, failure);
}

/// <summary>
/// Places orders from published sites and reads them back for the owner's inbox.
/// <para>
/// The counterpart of <c>LeadsService</c>, and it assumes the same thing: the tenant is already in
/// scope. Placing runs under the tenant resolved from the site's host; the inbox scopes the tenant
/// through <c>SiteManagementService.LoadAsync</c>. The query filter does the rest.
/// </para>
/// </summary>
public sealed class OrdersService(WebsiteBuilderDbContext db, IOrderNotifier notifier)
{
    /// <summary>
    /// How many times to re-roll a colliding reference before giving up on a pretty one.
    /// <para>
    /// Six characters from a 29-letter alphabet is about 594 million codes, so a collision needs a
    /// catalog nobody in this market has. The retry exists so that if one ever does happen it costs
    /// a re-roll rather than an error page between a customer and their order.
    /// </para>
    /// </summary>
    private const int ReferenceAttempts = 5;

    /// <summary>
    /// Commits the order, then tells the owner. In that order, and for the same reason a lead is:
    /// the customer has finished their part, so a mail provider being unreachable must not lose it.
    /// </summary>
    public async Task<OrderResult> PlaceAsync(
        Guid siteId,
        IReadOnlyList<PricedLine> lines,
        string? customerName,
        string? phone,
        string? email,
        string? note,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return OrderResult.Failed(OrderFailure.EmptyCart);
        }

        customerName = customerName?.Trim();
        phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        // A name and one way to reply. Same rule as a lead, and for the same reason: the owner
        // fulfils this by contacting the customer, so an order without that is unfulfillable.
        if (string.IsNullOrWhiteSpace(customerName) || (phone is null && email is null))
        {
            return OrderResult.Failed(OrderFailure.MissingDetails);
        }

        var (total, currency) = OrderMessage.Total(lines);

        var order = new Order
        {
            SiteId = siteId,
            Reference = await UniqueReferenceAsync(cancellationToken),
            CustomerName = Clamp(customerName, 200),
            PhoneNumber = phone is null ? null : Clamp(phone, 40),
            Email = email is null ? null : Clamp(email, 200),
            Note = note is null ? null : Clamp(note, 2000),
            TotalMinor = total,
            Currency = currency,
            // Every field below is copied, not referenced — see the note on OrderItem.
            Items = [.. lines.Select(line => new OrderItem
            {
                ProductId = line.Product.Id,
                ProductName = Clamp(line.Product.Name, 200),
                UnitPriceMinor = line.Product.PriceMinor,
                Currency = line.Product.Currency,
                Quantity = line.Quantity,
            })],
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        await notifier.NotifyAsync(order, cancellationToken);

        return OrderResult.Success(order);
    }

    /// <summary>
    /// One order by the reference the customer was given. Used by the confirmation page, which an
    /// anonymous visitor reaches — so it is scoped to the tenant by the filter and to the site
    /// here, and returns nothing at all rather than hinting that a reference exists elsewhere.
    /// </summary>
    public Task<Order?> ByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        db.Orders
            .AsNoTracking()
            .Where(o => o.Reference == reference.ToUpperInvariant())
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>All of the site's orders, newest first. Tenant must already be in scope.</summary>
    public Task<List<Order>> ListForSiteAsync(Guid siteId, CancellationToken cancellationToken = default) =>
        db.Orders
            .AsNoTracking()
            .Where(o => o.SiteId == siteId)
            .OrderByDescending(o => o.CreatedUtc)
            .ToListAsync(cancellationToken);

    /// <summary>Marks every unread order for the site as read. Tenant must already be in scope.</summary>
    public async Task MarkAllReadAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        // ExecuteUpdate rather than loading them: this runs on every visit to the inbox, and the
        // rows are not otherwise needed. Owned items are untouched, so nothing cascades.
        await db.Orders
            .Where(o => o.SiteId == siteId && !o.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.IsRead, true), cancellationToken);
    }

    /// <summary>Moves an order along. The owner does this by hand — nothing else can know.</summary>
    public async Task<bool> SetStatusAsync(
        Guid orderId, OrderStatus status, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return false;
        }

        order.Status = status;
        order.UpdatedUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task<string> UniqueReferenceAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < ReferenceAttempts; attempt++)
        {
            var candidate = Order.NewReference();

            if (!await db.Orders.AnyAsync(o => o.Reference == candidate, cancellationToken))
            {
                return candidate;
            }
        }

        // Five collisions in a row is not chance, so stop trying to be readable and be correct:
        // a longer code the customer rarely has to read beats failing the order outright.
        return $"{Order.NewReference()}{Order.NewReference()}";
    }

    private static string Clamp(string value, int max) => value.Length <= max ? value : value[..max];
}
