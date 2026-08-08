namespace WebsiteBuilder.Web.Email;

/// <summary>An outgoing message. Plain text alongside HTML so it survives clients that refuse HTML.</summary>
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Sends transactional email: magic-link sign-in (WB-15) and new-lead notifications (WB-32).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>SMTP host. Blank means no provider is configured and the log sender is used instead.</summary>
    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;
    public string? SmtpUser { get; set; }
    public string? SmtpPassword { get; set; }
    public bool UseStartTls { get; set; } = true;

    /// <summary>
    /// The From address. Must be one the SMTP provider has authorised for a domain we own, or every
    /// message bounces. Deliberately has no default: a plausible-looking fallback would be a domain
    /// belonging to somebody else, and the failure only shows up as mail that silently never lands.
    /// </summary>
    public string FromAddress { get; set; } = "";

    /// <summary>Display name on outgoing mail. Blank sends the address alone, which is valid.</summary>
    public string FromName { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SmtpHost);
}
