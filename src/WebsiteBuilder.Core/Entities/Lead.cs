using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Core.Entities;

/// <summary>How a lead reached the business. Drives the channel badge in the inbox.</summary>
public enum LeadChannel
{
    ContactForm,
    WhatsApp,
    Call,
    Other,
}

/// <summary>
/// An enquiry from a visitor on a published site. Tenant-owned so it is captured under the site's
/// tenant and only ever read back by that tenant's owner. Created by the public contact form (WB-31)
/// and surfaced in the leads inbox (WB-32).
/// </summary>
public class Lead : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }

    public string Name { get; set; } = "";
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string Message { get; set; } = "";

    public LeadChannel Channel { get; set; } = LeadChannel.ContactForm;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Cleared when the owner opens the inbox; drives the "new" count and unread dot.</summary>
    public bool IsRead { get; set; }
}
