using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Auth;
using WebsiteBuilder.Web.Email;
using WebsiteBuilder.Web.Platform;

namespace WebsiteBuilder.Tests;

/// <summary>Captures what would have been emailed so tests can read the link out of it.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    /// <summary>Set to make sending blow up, for the "provider is down" path.</summary>
    public bool ThrowOnSend { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSend)
        {
            throw new InvalidOperationException("SMTP is unavailable.");
        }

        Sent.Add(message);
        return Task.CompletedTask;
    }
}

[Collection(nameof(PostgresCollection))]
public class SignInTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        OwnerSignInService Service,
        CapturingEmailSender Email,
        FakeTimeProvider Clock,
        WebsiteBuilderDbContext Db) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private Harness CreateHarness(AuthOptions? options = null)
    {
        var db = fixture.CreateContext(new TenantContext());
        var email = new CapturingEmailSender();
        var clock = new FakeTimeProvider(Now);

        var urls = new PlatformUrls(
            new HttpContextAccessor(),
            Options.Create(new PlatformOptions { PublicBaseUrl = "https://csbuild.test" }),
            Options.Create(new TenantResolutionOptions()));

        var service = new OwnerSignInService(
            db,
            email,
            urls,
            Options.Create(options ?? new AuthOptions()),
            clock,
            NullLogger<OwnerSignInService>.Instance);

        return new Harness(service, email, clock, db);
    }

    private static string Address() => $"owner-{Guid.NewGuid():N}@example.com";

    /// <summary>Pulls the token out of the emailed link, the way a real user clicks it.</summary>
    private static string TokenFrom(EmailMessage message)
    {
        var marker = "auth/verify?token=";
        var start = message.TextBody.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = message.TextBody.IndexOfAny([' ', '\r', '\n'], start);

        return Uri.UnescapeDataString(end < 0 ? message.TextBody[start..] : message.TextBody[start..end]);
    }

    [Fact]
    public async Task A_sign_in_link_creates_the_owner_on_first_use()
    {
        using var h = CreateHarness();
        var email = Address();

        Assert.Equal(SignInLinkResult.Sent, await h.Service.SendLinkAsync(email, returnUrl: null));

        var redeemed = await h.Service.RedeemAsync(TokenFrom(h.Email.Sent.Single()));

        Assert.NotNull(redeemed);
        Assert.Equal(email, redeemed!.Value.Owner.Email);
        Assert.Equal(Now, redeemed.Value.Owner.LastSignedInUtc);
    }

    [Fact]
    public async Task A_link_works_only_once()
    {
        using var h = CreateHarness();
        await h.Service.SendLinkAsync(Address(), returnUrl: null);
        var token = TokenFrom(h.Email.Sent.Single());

        Assert.NotNull(await h.Service.RedeemAsync(token));

        // A magic link lands in an inbox that may be synced, forwarded or archived; replay has to
        // be impossible rather than merely unlikely.
        Assert.Null(await h.Service.RedeemAsync(token));
    }

    [Fact]
    public async Task A_link_stops_working_once_it_expires()
    {
        using var h = CreateHarness();
        await h.Service.SendLinkAsync(Address(), returnUrl: null);
        var token = TokenFrom(h.Email.Sent.Single());

        h.Clock.Advance(SignInToken.Lifetime + TimeSpan.FromSeconds(1));

        Assert.Null(await h.Service.RedeemAsync(token));
    }

    [Fact]
    public async Task A_link_still_works_just_before_it_expires()
    {
        using var h = CreateHarness();
        await h.Service.SendLinkAsync(Address(), returnUrl: null);
        var token = TokenFrom(h.Email.Sent.Single());

        h.Clock.Advance(SignInToken.Lifetime - TimeSpan.FromSeconds(1));

        Assert.NotNull(await h.Service.RedeemAsync(token));
    }

    [Fact]
    public async Task An_unknown_token_is_rejected()
    {
        using var h = CreateHarness();

        Assert.Null(await h.Service.RedeemAsync("not-a-real-token"));
        Assert.Null(await h.Service.RedeemAsync(""));
        Assert.Null(await h.Service.RedeemAsync(null));
    }

    [Fact]
    public async Task The_plaintext_token_is_never_stored()
    {
        using var h = CreateHarness();
        var email = Address();
        await h.Service.SendLinkAsync(email, returnUrl: null);
        var token = TokenFrom(h.Email.Sent.Single());

        var stored = await h.Db.SignInTokens.AsNoTracking().SingleAsync(t => t.Email == email);

        Assert.NotEqual(token, stored.TokenHash);
        Assert.Equal(SignInToken.Hash(token), stored.TokenHash);
    }

    [Fact]
    public async Task Requesting_too_many_links_is_rate_limited()
    {
        using var h = CreateHarness(new AuthOptions { MaxLinksPerWindow = 3 });
        var email = Address();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(SignInLinkResult.Sent, await h.Service.SendLinkAsync(email, returnUrl: null));
        }

        Assert.Equal(SignInLinkResult.RateLimited, await h.Service.SendLinkAsync(email, returnUrl: null));
        Assert.Equal(3, h.Email.Sent.Count);
    }

    [Fact]
    public async Task The_rate_limit_lifts_once_the_window_passes()
    {
        var options = new AuthOptions { MaxLinksPerWindow = 2, RateLimitWindow = TimeSpan.FromMinutes(15) };
        using var h = CreateHarness(options);
        var email = Address();

        await h.Service.SendLinkAsync(email, returnUrl: null);
        await h.Service.SendLinkAsync(email, returnUrl: null);
        Assert.Equal(SignInLinkResult.RateLimited, await h.Service.SendLinkAsync(email, returnUrl: null));

        h.Clock.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(SignInLinkResult.Sent, await h.Service.SendLinkAsync(email, returnUrl: null));
    }

    [Fact]
    public async Task The_rate_limit_is_per_address()
    {
        using var h = CreateHarness(new AuthOptions { MaxLinksPerWindow = 1 });

        Assert.Equal(SignInLinkResult.Sent, await h.Service.SendLinkAsync(Address(), returnUrl: null));
        Assert.Equal(SignInLinkResult.Sent, await h.Service.SendLinkAsync(Address(), returnUrl: null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nope")]
    [InlineData("no-at-sign.example.com")]
    [InlineData("two@at@example.com")]
    [InlineData("spaces in@example.com")]
    public async Task A_malformed_address_is_refused(string? candidate)
    {
        using var h = CreateHarness();

        Assert.Equal(SignInLinkResult.InvalidEmail, await h.Service.SendLinkAsync(candidate, returnUrl: null));
        Assert.Empty(h.Email.Sent);
    }

    [Fact]
    public async Task A_failure_to_send_is_reported_rather_than_thrown()
    {
        using var h = CreateHarness();
        h.Email.ThrowOnSend = true;

        Assert.Equal(SignInLinkResult.SendFailed, await h.Service.SendLinkAsync(Address(), returnUrl: null));
    }

    [Fact]
    public async Task Signing_in_with_Google_and_a_link_reaches_the_same_owner()
    {
        using var h = CreateHarness();
        var email = Address();

        var viaGoogle = await h.Service.FromGoogleAsync(email, "Kwame O.", "google-subject-1");

        await h.Service.SendLinkAsync(email, returnUrl: null);
        var viaLink = await h.Service.RedeemAsync(TokenFrom(h.Email.Sent.Single()));

        // Email is the identity: two routes to the same address must not create two accounts.
        Assert.Equal(viaGoogle.Id, viaLink!.Value.Owner.Id);
        Assert.Equal(1, await h.Db.Owners.CountAsync(o => o.Email == email));
    }

    [Fact]
    public async Task Google_sign_in_is_case_insensitive_about_the_address()
    {
        using var h = CreateHarness();
        var email = Address();

        var first = await h.Service.FromGoogleAsync(email.ToUpperInvariant(), "Kwame", "sub-1");
        var second = await h.Service.FromGoogleAsync(email, "Kwame", "sub-1");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(email, second.Email);
    }

    [Fact]
    public async Task Google_records_the_subject_and_fills_a_blank_name()
    {
        using var h = CreateHarness();
        var email = Address();

        // Magic link first, so the owner exists with no name.
        await h.Service.SendLinkAsync(email, returnUrl: null);
        await h.Service.RedeemAsync(TokenFrom(h.Email.Sent.Single()));

        var owner = await h.Service.FromGoogleAsync(email, "Ama Boateng", "google-subject-9");

        Assert.Equal("Ama Boateng", owner.Name);
        Assert.Equal("google-subject-9", owner.GoogleSubject);
    }

    [Fact]
    public async Task A_name_already_on_the_account_is_not_overwritten_by_Google()
    {
        using var h = CreateHarness();
        var email = Address();

        await h.Service.FromGoogleAsync(email, "Original Name", "sub-1");
        var owner = await h.Service.FromGoogleAsync(email, "Different Name", "sub-1");

        Assert.Equal("Original Name", owner.Name);
    }

    [Fact]
    public async Task The_return_url_survives_the_round_trip()
    {
        using var h = CreateHarness();

        await h.Service.SendLinkAsync(Address(), "/manage/abc/edit");
        var redeemed = await h.Service.RedeemAsync(TokenFrom(h.Email.Sent.Single()));

        Assert.Equal("/manage/abc/edit", redeemed!.Value.ReturnUrl);
    }

    [Theory]
    [InlineData("https://evil.example.com", null)]
    [InlineData("//evil.example.com", null)]
    [InlineData("/\\evil.example.com", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("/dashboard", "/dashboard")]
    [InlineData("/manage/1/leads?x=1", "/manage/1/leads?x=1")]
    public void Only_same_site_return_urls_are_kept(string? candidate, string? expected)
    {
        // An unchecked return URL turns sign-in into an open redirect, which is exactly the shape
        // phishing wants: a real domain in the link, someone else's page at the end of it.
        Assert.Equal(expected, OwnerSignInService.SafeReturnUrl(candidate));
    }

    [Fact]
    public async Task An_off_site_return_url_is_dropped_before_it_is_stored()
    {
        using var h = CreateHarness();

        await h.Service.SendLinkAsync(Address(), "https://evil.example.com/steal");
        var redeemed = await h.Service.RedeemAsync(TokenFrom(h.Email.Sent.Single()));

        Assert.Null(redeemed!.Value.ReturnUrl);
    }
}
