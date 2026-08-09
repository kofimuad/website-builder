using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebsiteBuilder.Web.Email;

namespace WebsiteBuilder.Tests;

/// <summary>
/// The HTTPS path, against a stub transport. It exists because Railway drops outbound SMTP below
/// the Pro plan; these tests pin the request Resend actually needs to see.
/// </summary>
public class ResendEmailSenderTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public Uri? LastUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static readonly EmailMessage Message = new(
        "owner@example.com", "Your CS Build sign-in link", "<p>Hello</p>", "Hello");

    private static (ResendEmailSender Sender, StubHandler Handler) Build(
        EmailOptions? options = null,
        HttpStatusCode status = HttpStatusCode.OK,
        string body = """{"id":"3f5a1d0c"}""")
    {
        var handler = new StubHandler(status, body);
        var settings = options ?? new EmailOptions { ApiKey = "re_test", FromAddress = "no-reply@csbuild.app" };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com/") };

        return (new ResendEmailSender(http, Options.Create(settings), NullLogger<ResendEmailSender>.Instance), handler);
    }

    [Fact]
    public async Task The_message_is_posted_in_the_shape_resend_expects()
    {
        var (sender, handler) = Build();

        await sender.SendAsync(Message);

        Assert.Equal("https://api.resend.com/emails", handler.LastUri!.ToString());

        using var sent = JsonDocument.Parse(handler.LastBody!);
        var root = sent.RootElement;

        Assert.Equal("no-reply@csbuild.app", root.GetProperty("from").GetString());
        Assert.Equal("owner@example.com", root.GetProperty("to")[0].GetString());
        Assert.Equal("Your CS Build sign-in link", root.GetProperty("subject").GetString());
        Assert.Equal("<p>Hello</p>", root.GetProperty("html").GetString());
        // Plain text alongside HTML, so the message survives a client that refuses HTML.
        Assert.Equal("Hello", root.GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_display_name_is_sent_in_the_form_resend_parses()
    {
        var (sender, handler) = Build(new EmailOptions
        {
            ApiKey = "re_test",
            FromAddress = "no-reply@csbuild.app",
            FromName = "CS Build",
        });

        await sender.SendAsync(Message);

        using var sent = JsonDocument.Parse(handler.LastBody!);

        Assert.Equal("CS Build <no-reply@csbuild.app>", sent.RootElement.GetProperty("from").GetString());
    }

    [Fact]
    public async Task A_rejection_keeps_resends_own_explanation()
    {
        // "The csbuild.app domain is not verified" is the entire diagnosis. Flattening it into
        // "sending failed" is how an afternoon disappears.
        const string error =
            """{"statusCode":403,"message":"The csbuild.app domain is not verified. Please verify it."}""";

        var (sender, _) = Build(status: HttpStatusCode.Forbidden, body: error);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message));

        Assert.Contains("403", exception.Message);
        Assert.Contains("not verified", exception.Message);
    }

    [Fact]
    public async Task A_failure_throws_so_the_caller_can_say_the_link_was_not_sent()
    {
        // OwnerSignInService turns this into SignInLinkResult.SendFailed, which is the only reason
        // the sign-in page can show an error rather than a confirmation for mail that never went.
        var (sender, _) = Build(status: HttpStatusCode.InternalServerError, body: "{}");

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message));
    }
}

public class EmailOptionsTests
{
    [Fact]
    public void An_api_key_takes_precedence_over_smtp_settings()
    {
        // Both set is the state a Railway project lands in while moving off SMTP. The API path is
        // the one that works there, so it must be the one chosen.
        var options = new EmailOptions
        {
            ApiKey = "re_test",
            SmtpHost = "smtp.resend.com",
            FromAddress = "no-reply@csbuild.app",
        };

        Assert.True(options.UsesApi);
        Assert.False(options.UsesSmtp);
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void Smtp_is_used_when_there_is_no_api_key()
    {
        var options = new EmailOptions { SmtpHost = "smtp.resend.com", FromAddress = "no-reply@csbuild.app" };

        Assert.True(options.UsesSmtp);
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void Neither_configured_means_mail_goes_to_the_log()
    {
        var options = new EmailOptions();

        Assert.False(options.IsConfigured);
        Assert.Contains("NOT SENT", options.Describe());
    }

    [Fact]
    public void The_startup_banner_names_the_path_that_is_live()
    {
        Assert.Contains("Resend API", new EmailOptions { ApiKey = "re_x", FromAddress = "a@b.c" }.Describe());
        Assert.Contains("smtp.resend.com:587", new EmailOptions { SmtpHost = "smtp.resend.com", FromAddress = "a@b.c" }.Describe());
    }
}
