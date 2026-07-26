using System.Security.Cryptography;
using System.Text;

namespace WebsiteBuilder.Core.Entities;

/// <summary>
/// A single-use magic-link token (WB-15). The row stores only a hash: the link is a bearer
/// credential, so a leaked database dump must not let anyone sign in as the addresses in it.
/// </summary>
public class SignInToken
{
    /// <summary>How long a link stays usable. Long enough to switch to a phone, short enough to matter.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The address the link was sent to, normalised. The owner may not exist yet.</summary>
    public required string Email { get; set; }

    /// <summary>SHA-256 of the token, hex encoded. Never the token itself.</summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresUtc { get; set; } = DateTimeOffset.UtcNow + Lifetime;

    /// <summary>Set the moment the link is redeemed. A non-null value means the link is spent.</summary>
    public DateTimeOffset? ConsumedUtc { get; set; }

    /// <summary>Where to send them after a successful sign-in. Validated as a local path before storage.</summary>
    public string? ReturnUrl { get; set; }

    public bool IsUsable(DateTimeOffset now) => ConsumedUtc is null && now < ExpiresUtc;

    /// <summary>Generates a fresh token. The plaintext is returned once, for the link, and never stored.</summary>
    public static (SignInToken Token, string Plaintext) Issue(string email, string? returnUrl, DateTimeOffset now)
    {
        // 32 bytes of CSPRNG output, base64url encoded so it survives a query string intact.
        var plaintext = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        var token = new SignInToken
        {
            Email = Owner.NormaliseEmail(email),
            TokenHash = Hash(plaintext),
            CreatedUtc = now,
            ExpiresUtc = now + Lifetime,
            ReturnUrl = returnUrl,
        };

        return (token, plaintext);
    }

    public static string Hash(string plaintext) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
