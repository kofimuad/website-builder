using System.Net;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Email;
using WebsiteBuilder.Web.Platform;

namespace WebsiteBuilder.Web.Leads;

/// <summary>Notifies the business owner that a new lead arrived (WB-32).</summary>
public interface ILeadNotifier
{
    Task NotifyAsync(Lead lead, CancellationToken cancellationToken = default);
}

/// <summary>
/// Emails the tenant's owner. Failures are logged and swallowed: the lead is already committed by
/// the time this runs, and a visitor who filled in a contact form must never see an error because
/// the business owner's mail provider was unreachable.
/// </summary>
public sealed class EmailLeadNotifier(
    WebsiteBuilderDbContext db,
    IEmailSender email,
    PlatformUrls urls,
    ILogger<EmailLeadNotifier> logger) : ILeadNotifier
{
    public async Task NotifyAsync(Lead lead, CancellationToken cancellationToken = default)
    {
        try
        {
            // Tenants and owners sit outside the tenant filter, so this reads them directly.
            var recipient = await db.Tenants
                .AsNoTracking()
                .Where(t => t.Id == lead.TenantId && t.OwnerId != null)
                .Join(db.Owners.AsNoTracking(), t => t.OwnerId, o => o.Id, (t, o) => new { t.Name, o.Email })
                .FirstOrDefaultAsync(cancellationToken);

            if (recipient is null)
            {
                // An unowned tenant — a pre-sign-in or seeded site. Nothing to send to, but the
                // lead is safe in the inbox, so this is information rather than a failure.
                logger.LogInformation(
                    "Lead {LeadId} captured for tenant {TenantId}, which has no owner to notify.",
                    lead.Id, lead.TenantId);
                return;
            }

            await email.SendAsync(Compose(lead, recipient.Name, recipient.Email), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not notify the owner about lead {LeadId}.", lead.Id);
        }
    }

    private EmailMessage Compose(Lead lead, string businessName, string to)
    {
        var inbox = urls.LeadsInbox(lead.SiteId);
        var reply = lead.PhoneNumber ?? lead.Email ?? "no contact details";

        var text =
            $"""
             You have a new enquiry from your {businessName} website.

             From:    {lead.Name}
             Contact: {reply}

             {lead.Message}

             Reply quickly — most people contact more than one business.
             See all your leads: {inbox}
             """;

        var html =
            $"""
             <p>You have a new enquiry from your <strong>{Escape(businessName)}</strong> website.</p>
             <p><strong>{Escape(lead.Name)}</strong><br>{Escape(reply)}</p>
             <blockquote style="margin:0;padding:12px 16px;border-left:3px solid #ddd;color:#333">{Escape(lead.Message)}</blockquote>
             <p>Reply quickly — most people contact more than one business.</p>
             <p><a href="{Escape(inbox)}">See all your leads</a></p>
             """;

        return new EmailMessage(to, $"New enquiry from {lead.Name}", html, text);
    }

    // Lead text is written by anonymous visitors and lands in the owner's mail client; escaping it
    // is what keeps a contact form from becoming an HTML injection into someone's inbox.
    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
