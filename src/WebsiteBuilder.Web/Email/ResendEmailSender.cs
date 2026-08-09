using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace WebsiteBuilder.Web.Email;

/// <summary>
/// Sends through Resend's HTTPS API rather than SMTP.
/// <para>
/// The original decision here was SMTP over a vendor API, so that changing provider stayed a
/// configuration change. That reasoning was sound and the hosting environment overruled it:
/// <b>Railway disables outbound SMTP below the Pro plan</b>, and does it by dropping the packets,
/// so a send to port 587 sits there until the socket times out — minutes later, with the owner
/// looking at a confirmation screen and an inbox that stays empty. Port 443 is never blocked.
/// </para>
/// <para>
/// <see cref="SmtpEmailSender"/> is still here and still selected by <c>Email:SmtpHost</c>, so
/// moving to a host that allows SMTP, or to a provider without an HTTP API, remains configuration.
/// </para>
/// </summary>
public sealed class ResendEmailSender(
    HttpClient http,
    IOptions<EmailOptions> options,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject
        {
            ["from"] = string.IsNullOrWhiteSpace(_options.FromName)
                ? _options.FromAddress
                : $"{_options.FromName} <{_options.FromAddress}>",
            ["to"] = new JsonArray(message.To),
            ["subject"] = message.Subject,
            ["html"] = message.HtmlBody,
            ["text"] = message.TextBody,
        };

        try
        {
            using var response = await http.PostAsJsonAsync("emails", payload, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Resend names the actual problem — an unverified domain, a from-address that is
                // not on it, a revoked key. That sentence is the whole diagnosis, so it goes in
                // the exception rather than being flattened into "sending failed".
                throw new InvalidOperationException(
                    $"Resend returned {(int)response.StatusCode}: {body}");
            }

            logger.LogInformation(
                "Sent {Subject} to {To} as {From}.", message.Subject, message.To, _options.FromAddress);
        }
        catch (Exception ex)
        {
            // Never let a mail failure take down the thing that triggered it: a lead must still be
            // saved if the notification bounces. The caller decides what to tell the user.
            logger.LogError(
                ex,
                "Failed to send {Subject} to {To} via the Resend API as {From}.",
                message.Subject, message.To, _options.FromAddress);

            throw;
        }
    }
}
