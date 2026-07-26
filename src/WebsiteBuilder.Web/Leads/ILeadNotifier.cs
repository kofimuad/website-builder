using WebsiteBuilder.Core.Entities;

namespace WebsiteBuilder.Web.Leads;

/// <summary>
/// Notifies the business owner that a new lead arrived. WB-32 calls for email; until an email
/// provider is wired (SMTP / a service like Resend), <see cref="LogLeadNotifier"/> records it so
/// nothing is lost and the seam is ready.
/// </summary>
public interface ILeadNotifier
{
    Task NotifyAsync(Lead lead, CancellationToken cancellationToken = default);
}

/// <summary>Records new leads to the log. Swap for an email-sending implementation to finish WB-32.</summary>
public sealed class LogLeadNotifier(ILogger<LogLeadNotifier> logger) : ILeadNotifier
{
    public Task NotifyAsync(Lead lead, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "New lead for tenant {TenantId} on site {SiteId}: {Name} via {Channel}. Contact: {Phone} {Email}.",
            lead.TenantId, lead.SiteId, lead.Name, lead.Channel, lead.PhoneNumber, lead.Email);
        return Task.CompletedTask;
    }
}
