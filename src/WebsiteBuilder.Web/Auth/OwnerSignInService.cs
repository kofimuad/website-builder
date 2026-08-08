using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Email;
using WebsiteBuilder.Web.Platform;

namespace WebsiteBuilder.Web.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Google OAuth client id. Blank disables the Google button entirely; magic link still works.</summary>
    public string? GoogleClientId { get; set; }

    public string? GoogleClientSecret { get; set; }

    /// <summary>How many sign-in links one address may request per <see cref="RateLimitWindow"/>.</summary>
    public int MaxLinksPerWindow { get; set; } = 5;

    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(15);

    public bool IsGoogleConfigured =>
        !string.IsNullOrWhiteSpace(GoogleClientId) && !string.IsNullOrWhiteSpace(GoogleClientSecret);
}

/// <summary>Why a magic link could not be issued. Distinguishes cases the UI must word differently.</summary>
public enum SignInLinkResult
{
    Sent,
    InvalidEmail,
    RateLimited,
    SendFailed,
}

/// <summary>
/// Issues and redeems magic links and resolves owners for both sign-in routes (WB-15).
/// Kept out of the endpoint bodies so the rules — single use, expiry, rate limit, email as
/// identity — are testable without an HTTP pipeline.
/// </summary>
public sealed class OwnerSignInService(
    WebsiteBuilderDbContext db,
    IEmailSender email,
    PlatformUrls urls,
    Microsoft.Extensions.Options.IOptions<AuthOptions> options,
    TimeProvider timeProvider,
    ILogger<OwnerSignInService> logger)
{
    private readonly AuthOptions _options = options.Value;

    public async Task<SignInLinkResult> SendLinkAsync(
        string? emailAddress,
        string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        if (!LooksLikeEmail(emailAddress))
        {
            return SignInLinkResult.InvalidEmail;
        }

        var normalised = Owner.NormaliseEmail(emailAddress!);
        var now = timeProvider.GetUtcNow();

        var recent = await db.SignInTokens
            .CountAsync(t => t.Email == normalised && t.CreatedUtc > now - _options.RateLimitWindow, cancellationToken);

        if (recent >= _options.MaxLinksPerWindow)
        {
            // Deliberately not surfaced as "this address exists": the caller shows the same
            // "check your email" screen either way so the endpoint cannot enumerate accounts.
            logger.LogWarning("Rate limited sign-in links for {Email}.", normalised);
            return SignInLinkResult.RateLimited;
        }

        var (token, plaintext) = SignInToken.Issue(normalised, SafeReturnUrl(returnUrl), now);
        db.SignInTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await email.SendAsync(Compose(normalised, urls.MagicLink(plaintext)), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send a sign-in link to {Email}.", normalised);
            return SignInLinkResult.SendFailed;
        }

        return SignInLinkResult.Sent;
    }

    /// <summary>
    /// Redeems a link. Returns the owner and where to send them, or null if the token is unknown,
    /// expired or already used. Consuming and creating the owner happen in one save so a link can
    /// never be spent without producing an account.
    /// </summary>
    public async Task<(Owner Owner, string? ReturnUrl)?> RedeemAsync(
        string? plaintext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return null;
        }

        var hash = SignInToken.Hash(plaintext);
        var token = await db.SignInTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (token is null || !token.IsUsable(now))
        {
            return null;
        }

        token.ConsumedUtc = now;
        var owner = await UpsertAsync(token.Email, name: null, googleSubject: null, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return (owner, token.ReturnUrl);
    }

    /// <summary>Finds or creates the owner behind a Google sign-in and records the subject claim.</summary>
    public async Task<Owner> FromGoogleAsync(
        string emailAddress,
        string? name,
        string? googleSubject,
        CancellationToken cancellationToken = default)
    {
        var owner = await UpsertAsync(
            Owner.NormaliseEmail(emailAddress), name, googleSubject, timeProvider.GetUtcNow(), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return owner;
    }

    private async Task<Owner> UpsertAsync(
        string normalisedEmail,
        string? name,
        string? googleSubject,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var owner = await db.Owners.FirstOrDefaultAsync(o => o.Email == normalisedEmail, cancellationToken);

        if (owner is null)
        {
            owner = new Owner { Email = normalisedEmail, Name = name ?? "", CreatedUtc = now };
            db.Owners.Add(owner);
        }
        else if (!string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(owner.Name))
        {
            // Fill a blank name, never overwrite one the owner may have set deliberately.
            owner.Name = name;
        }

        if (googleSubject is not null)
        {
            owner.GoogleSubject = googleSubject;
        }

        owner.LastSignedInUtc = now;
        return owner;
    }

    private EmailMessage Compose(string to, string link) => new(
        to,
        $"Your {Branding.Name} sign-in link",
        $"""
         <p>Click the button below to sign in to {Branding.Name}. The link works once and expires in {SignInToken.Lifetime.TotalMinutes:0} minutes.</p>
         <p><a href="{link}" style="display:inline-block;padding:12px 20px;background:#1f5eff;color:#fff;border-radius:8px;text-decoration:none">Sign in to {Branding.Name}</a></p>
         <p style="color:#666;font-size:13px">If you did not ask for this, you can ignore this email — nobody can sign in without the link.</p>
         """,
        $"""
         Click the link below to sign in to {Branding.Name}. It works once and expires in {SignInToken.Lifetime.TotalMinutes:0} minutes.

         {link}

         If you did not ask for this, you can ignore this email — nobody can sign in without the link.
         """);

    /// <summary>
    /// Only same-site paths survive. A return URL arrives from the query string, so echoing it back
    /// into a redirect unchecked would turn sign-in into an open redirect for phishing.
    /// </summary>
    public static string? SafeReturnUrl(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !candidate.StartsWith('/'))
        {
            return null;
        }

        // "//host" and "/\host" are protocol-relative: the browser reads them as another origin.
        return candidate.StartsWith("//") || candidate.StartsWith("/\\") ? null : candidate;
    }

    private static bool LooksLikeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var at = trimmed.IndexOf('@');

        // Deliberately loose. The real proof that an address works is that the link arrives;
        // a stricter pattern only ever rejects valid addresses.
        return at > 0
            && at < trimmed.Length - 1
            && trimmed.IndexOf('@', at + 1) < 0
            && trimmed.Contains('.', StringComparison.Ordinal)
            && !trimmed.Any(char.IsWhiteSpace)
            && trimmed.Length <= 320;
    }
}
