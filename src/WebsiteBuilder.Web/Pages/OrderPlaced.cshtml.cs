using Microsoft.AspNetCore.Mvc;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Shop;

namespace WebsiteBuilder.Web.Pages;

/// <summary>
/// The "we've got your order" page, reached by reference after checkout.
/// <para>
/// Addressed by the reference rather than an id so the customer can come back to it from their
/// browser history, and so the link means something if they paste it into a chat with the owner.
/// It shows what was ordered — from the order's own snapshot, so it keeps saying what the customer
/// agreed to even after the owner re-prices something — and offers the WhatsApp message as a
/// nudge, not as the order itself.
/// </para>
/// </summary>
public class OrderPlacedModel(WebsiteBuilderDbContext db, OrdersService orders) : ShopPageModel(db)
{
    public Order Order { get; private set; } = null!;

    /// <summary>The wa.me link telling the owner it has been placed, or null when there is no number.</summary>
    public string? WhatsAppLink { get; private set; }

    public async Task<IActionResult> OnGetAsync(string reference)
    {
        if (await LoadChromeAsync() is { } refusal)
        {
            return refusal;
        }

        if (string.IsNullOrWhiteSpace(reference))
        {
            return NotFound();
        }

        var order = await orders.ByReferenceAsync(reference, HttpContext.RequestAborted);

        if (order is null)
        {
            return NotFound();
        }

        Order = order;

        if (!string.IsNullOrWhiteSpace(Chrome.Contact?.WhatsAppNumber))
        {
            WhatsAppLink = OrderMessage.Link(
                Chrome.Contact.WhatsAppNumber,
                OrderMessage.ComposePlaced(Chrome.BusinessName, order));
        }

        return Page();
    }
}
