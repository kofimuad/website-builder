using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Email;
using WebsiteBuilder.Web.Platform;

namespace WebsiteBuilder.Web.Shop;

/// <summary>Tells the business owner that an order came in.</summary>
public interface IOrderNotifier
{
    Task NotifyAsync(Order order, CancellationToken cancellationToken = default);
}

/// <summary>
/// Emails the tenant's owner. Failures are logged and swallowed, exactly as for a lead: the order
/// is already committed by the time this runs, and a customer who has just placed one must never
/// see an error because the business owner's mail provider was unreachable.
/// </summary>
public sealed class EmailOrderNotifier(
    WebsiteBuilderDbContext db,
    IEmailSender email,
    PlatformUrls urls,
    ILogger<EmailOrderNotifier> logger) : IOrderNotifier
{
    public async Task NotifyAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        try
        {
            // Tenants and owners sit outside the tenant filter, so this reads them directly.
            var recipient = await db.Tenants
                .AsNoTracking()
                .Where(t => t.Id == order.TenantId && t.OwnerId != null)
                .Join(db.Owners.AsNoTracking(), t => t.OwnerId, o => o.Id, (t, o) => new { t.Name, o.Email })
                .FirstOrDefaultAsync(cancellationToken);

            if (recipient is null)
            {
                // An unowned tenant — a seeded or pre-sign-in site. Nothing to send to, but the
                // order is safe in the inbox, so this is information rather than a failure.
                logger.LogInformation(
                    "Order {Reference} placed for tenant {TenantId}, which has no owner to notify.",
                    order.Reference, order.TenantId);
                return;
            }

            await email.SendAsync(Compose(order, recipient.Name, recipient.Email), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not notify the owner about order {Reference}.", order.Reference);
        }
    }

    private EmailMessage Compose(Order order, string businessName, string to)
    {
        var inbox = urls.OrdersInbox(order.SiteId);
        var reply = order.PhoneNumber ?? order.Email ?? "no contact details";
        var total = order.TotalMinor is { } amount && order.Currency is { } currency
            ? OrderMessage.FormatMoney(amount, currency)
            : "to be confirmed";

        var textLines = new StringBuilder();
        var htmlRows = new StringBuilder();

        foreach (var item in order.Items)
        {
            var price = item.LineTotalMinor is { } line
                ? OrderMessage.FormatMoney(line, item.Currency)
                : "price on request";

            textLines.Append("  ").Append(item.Quantity).Append(" x ")
                .Append(item.ProductName).Append(" — ").Append(price).Append('\n');

            htmlRows.Append("<tr><td style=\"padding:4px 12px 4px 0\">")
                .Append(item.Quantity).Append(" &times; ").Append(Escape(item.ProductName))
                .Append("</td><td style=\"padding:4px 0\">").Append(Escape(price)).Append("</td></tr>");
        }

        var text =
            $"""
             You have a new order from your {businessName} website.

             Order:   {order.Reference}
             From:    {order.CustomerName}
             Contact: {reply}

             {textLines.ToString().TrimEnd()}

             Total: {total}
             {(order.Note is null ? "" : $"\nNote from the customer:\n{order.Note}\n")}
             Contact them to confirm the order and arrange payment — nothing has been paid yet.
             See all your orders: {inbox}
             """;

        var html =
            $"""
             <p>You have a new order from your <strong>{Escape(businessName)}</strong> website.</p>
             <p>Order <strong>{Escape(order.Reference)}</strong><br>
             {Escape(order.CustomerName)}<br>{Escape(reply)}</p>
             <table style="border-collapse:collapse">{htmlRows}</table>
             <p><strong>Total: {Escape(total)}</strong></p>
             {(order.Note is null ? "" : $"<blockquote style=\"margin:0;padding:12px 16px;border-left:3px solid #ddd;color:#333\">{Escape(order.Note)}</blockquote>")}
             <p>Contact them to confirm the order and arrange payment &mdash; nothing has been paid yet.</p>
             <p><a href="{Escape(inbox)}">See all your orders</a></p>
             """;

        return new EmailMessage(to, $"New order {order.Reference} from {order.CustomerName}", html, text);
    }

    // Every string here was typed by an anonymous visitor and lands in the owner's mail client.
    // Escaping is what stops a checkout form becoming an HTML injection into someone's inbox.
    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
