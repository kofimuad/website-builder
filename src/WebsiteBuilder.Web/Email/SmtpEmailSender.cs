using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace WebsiteBuilder.Web.Email;

/// <summary>
/// Sends over SMTP, which every provider (Resend, Postmark, SES, Gmail) speaks. Chosen over a
/// vendor SDK so switching provider is configuration rather than a code change.
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl = _options.UseStartTls,
            Credentials = string.IsNullOrWhiteSpace(_options.SmtpUser)
                ? null
                : new NetworkCredential(_options.SmtpUser, _options.SmtpPassword),
        };

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = message.Subject,
            Body = message.TextBody,
            IsBodyHtml = false,
        };

        mail.To.Add(message.To);
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            message.HtmlBody, null, "text/html"));

        try
        {
            await client.SendMailAsync(mail, cancellationToken);
        }
        catch (Exception ex)
        {
            // Never let a mail failure take down the thing that triggered it: a lead must still be
            // saved if the notification bounces, and a sign-in link the user can request again is
            // a better outcome than a 500. The caller decides what to tell the user.
            logger.LogError(ex, "Failed to send {Subject} to {To}.", message.Subject, message.To);
            throw;
        }
    }
}

/// <summary>
/// Writes email to the log instead of sending it. Used when no SMTP host is configured, so a
/// developer can sign in locally by copying the magic link out of the console.
/// </summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "EMAIL (not sent — no SMTP configured)\n  To: {To}\n  Subject: {Subject}\n{Body}",
            message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }
}
