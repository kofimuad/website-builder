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

    /// <summary>
    /// A Resend API key (<c>re_…</c>), which sends over HTTPS instead of SMTP.
    /// <para>
    /// This is the preferred path, and not for elegance: <b>Railway disables outbound SMTP on
    /// every plan below Pro</b>. Connections to port 587 are dropped rather than refused, so the
    /// symptom is a socket that times out minutes later and mail that never arrives. Port 443
    /// always works.
    /// </para>
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>SMTP host, for hosts that permit outbound SMTP. Ignored when <see cref="ApiKey"/> is set.</summary>
    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;
    public string? SmtpUser { get; set; }
    public string? SmtpPassword { get; set; }
    public bool UseStartTls { get; set; } = true;

    /// <summary>
    /// How long to wait for the provider before giving up. Someone is watching a spinner: a send
    /// that hangs for the four and a half minutes a blocked TCP connect takes is indistinguishable
    /// from the app being broken.
    /// </summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The From address. Must be one the SMTP provider has authorised for a domain we own, or every
    /// message bounces. Deliberately has no default: a plausible-looking fallback would be a domain
    /// belonging to somebody else, and the failure only shows up as mail that silently never lands.
    /// </summary>
    public string FromAddress { get; set; } = "";

    /// <summary>Display name on outgoing mail. Blank sends the address alone, which is valid.</summary>
    public string FromName { get; set; } = "";

    /// <summary>True when the API key is set, which takes precedence over any SMTP settings.</summary>
    public bool UsesApi => !string.IsNullOrWhiteSpace(ApiKey);

    public bool UsesSmtp => !UsesApi && !string.IsNullOrWhiteSpace(SmtpHost);

    /// <summary>False means mail is written to the log instead of sent.</summary>
    public bool IsConfigured => UsesApi || UsesSmtp;

    /// <summary>How the startup banner describes the live configuration.</summary>
    public string Describe() => UsesApi
        ? $"sending via the Resend API as {FromAddress}"
        : UsesSmtp
            ? $"sending via {SmtpHost}:{SmtpPort} as {FromAddress}"
            : "WRITTEN TO THIS LOG, NOT SENT — no Email:ApiKey or Email:SmtpHost configured";
}
