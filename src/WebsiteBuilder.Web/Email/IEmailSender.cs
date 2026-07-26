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

    /// <summary>The From address. Must be one the SMTP provider has authorised, or mail will bounce.</summary>
    public string FromAddress { get; set; } = "no-reply@sitely.app";

    public string FromName { get; set; } = "Sitely";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SmtpHost);
}
