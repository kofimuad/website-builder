namespace WebsiteBuilder.Core.Tenancy;

public sealed class TenantResolutionOptions
{
    public const string SectionName = "TenantResolution";

    /// <summary>The domain tenant subdomains hang off, e.g. "platform.com". No leading dot.</summary>
    public string PlatformDomain { get; set; } = "localhost";

    /// <summary>
    /// Subdomains the platform keeps for itself; these never resolve to a tenant.
    /// Configuration <em>adds</em> to this list rather than replacing it — the binder appends to
    /// the existing array — so these defaults cannot be switched off from appsettings, which is
    /// what we want given what they protect. See TenantResolutionOptionsBindingTests.
    /// </summary>
    /// <remarks>
    /// Three reasons a label belongs here, and all three are load-bearing:
    /// <list type="bullet">
    /// <item>It carries its own DNS records, so the wildcard does not apply to it and a tenant
    /// given the name would simply be unreachable. A DNS wildcard only answers for names with no
    /// records of any type, and mail verification puts records on real labels — Resend's MX and
    /// SPF live on <c>send</c>.</item>
    /// <item>It names something the platform serves itself, so a tenant would shadow it.</item>
    /// <item>It reads as a platform function on the platform's own domain. Handing
    /// <c>login.ourdomain.com</c> or <c>billing.ourdomain.com</c> to whoever signs up first is a
    /// ready-made phishing address that our certificate and our brand vouch for.</item>
    /// </list>
    /// Reserving costs nothing — no small business wants to be found at <c>secure.</c> — so this
    /// list errs wide.
    /// </remarks>
    public string[] ReservedSubdomains { get; set; } =
    [
        // The platform's own surface.
        "www", "app", "api", "admin", "dashboard", "static", "assets", "cdn",
        // Mail. These labels hold MX, SPF and DKIM records; a tenant here breaks either the
        // tenant's site or our ability to send sign-in links.
        "send", "mail", "smtp", "email", "mx", "bounces", "postmaster",
        // Anything a customer could mistake for us asking them for something.
        "login", "signin", "signup", "auth", "account", "accounts", "billing", "pay",
        "secure", "verify", "password", "support", "help",
        // Operations, so the names stay available when we need them.
        "status", "staging", "stage", "dev", "test", "preview", "docs", "blog",
    ];
}
