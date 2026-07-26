using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Data;

namespace WebsiteBuilder.Web.Leads;

/// <summary>
/// Captures enquiries from published sites and reads them back for the owner's inbox (WB-5).
/// Every method assumes the tenant is already in scope: capture runs under the tenant resolved
/// from the site's host, and the inbox scopes the tenant via <c>SiteManagementService.LoadAsync</c>.
/// </summary>
public sealed class LeadsService(WebsiteBuilderDbContext db, ILeadNotifier notifier)
{
    /// <summary>
    /// Stores a contact-form enquiry against the site and pings the notifier. Returns false when the
    /// submission is empty or has no way to reply — the form re-renders with a nudge rather than
    /// silently dropping it.
    /// </summary>
    public async Task<bool> CaptureAsync(
        Guid siteId,
        string? name,
        string? phone,
        string? email,
        string? message,
        CancellationToken cancellationToken = default)
    {
        name = name?.Trim();
        phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        message = message?.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        // At least one way to reply, otherwise the lead is a dead end.
        if (phone is null && email is null)
        {
            return false;
        }

        var lead = new Lead
        {
            SiteId = siteId,
            Name = Clamp(name, 200),
            PhoneNumber = phone is null ? null : Clamp(phone, 40),
            Email = email is null ? null : Clamp(email, 200),
            Message = Clamp(message, 4000),
            Channel = LeadChannel.ContactForm,
        };

        db.Leads.Add(lead);
        await db.SaveChangesAsync(cancellationToken);

        await notifier.NotifyAsync(lead, cancellationToken);
        return true;
    }

    /// <summary>All of the site's leads, newest first. Tenant must already be in scope.</summary>
    public Task<List<Lead>> ListForSiteAsync(Guid siteId, CancellationToken cancellationToken = default) =>
        db.Leads
            .AsNoTracking()
            .Where(l => l.SiteId == siteId)
            .OrderByDescending(l => l.CreatedUtc)
            .ToListAsync(cancellationToken);

    /// <summary>Marks every unread lead for the site as read. Tenant must already be in scope.</summary>
    public async Task MarkAllReadAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        await db.Leads
            .Where(l => l.SiteId == siteId && !l.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.IsRead, true), cancellationToken);
    }

    private static string Clamp(string value, int max) => value.Length <= max ? value : value[..max];
}
