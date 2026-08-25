using Microsoft.Extensions.Options;
using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Web.Platform;

public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    /// <summary>
    /// Absolute base URL of the builder app, e.g. "https://csbuild.app". Blank means "work it out
    /// from the current request", which is right locally but should be set in production so links
    /// in email are correct even when generated behind a proxy.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// A published site to show off from the landing page's "See a live example" button.
    /// <para>
    /// Configuration rather than a hard-coded address because the site worth showing differs by
    /// environment: locally it is the seeded demo, and in production it should be a real customer's
    /// site (with their agreement) or a demo tenant that has actually been published. Blank means
    /// there is nothing to show, and the button is <b>hidden</b> rather than rendered dead — which
    /// is what it had been doing, pointing at an <c>#example</c> anchor that was never on the page.
    /// </para>
    /// </summary>
    public string? ExampleSiteUrl { get; set; }
}

/// <summary>
/// Builds absolute builder-app URLs for links that leave the app — magic-link sign-in and lead
/// notification email. It deliberately does not use the current request's host: a lead is captured
/// on the tenant's own subdomain, and a link back to <c>joesplumbing.example.com/manage/…</c> would
/// 404 against the tenant-resolution middleware.
/// </summary>
public sealed class PlatformUrls(
    IHttpContextAccessor httpContextAccessor,
    IOptions<PlatformOptions> platformOptions,
    IOptions<TenantResolutionOptions> tenantOptions)
{
    public string BaseUrl
    {
        get
        {
            var configured = platformOptions.Value.PublicBaseUrl;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.TrimEnd('/');
            }

            var request = httpContextAccessor.HttpContext?.Request;
            var scheme = request?.Scheme ?? "https";
            var domain = tenantOptions.Value.PlatformDomain;

            // Keep the port the app is actually listening on, or local links become unreachable.
            var port = request?.Host.Port;
            var authority = port is null or 80 or 443 ? domain : $"{domain}:{port}";

            return $"{scheme}://{authority}";
        }
    }

    public string Absolute(string relativePath) => $"{BaseUrl}/{relativePath.TrimStart('/')}";

    /// <summary>
    /// The public address of a tenant's own site, e.g. "https://joesplumbing.csbuild.app".
    ///
    /// Scheme and port come from <see cref="PlatformOptions.PublicBaseUrl"/> when it is set,
    /// deliberately and not from the current request: this is called from inside a Blazor circuit
    /// where there is no <c>HttpContext</c> at all, so a request-derived scheme would silently
    /// become "https" with no port and produce a link that does not work locally.
    /// </summary>
    public string TenantSite(string subdomain)
    {
        var host = $"{subdomain}.{tenantOptions.Value.PlatformDomain}";
        var configured = platformOptions.Value.PublicBaseUrl;

        if (!string.IsNullOrWhiteSpace(configured)
            && Uri.TryCreate(configured, UriKind.Absolute, out var baseUri))
        {
            return baseUri.IsDefaultPort
                ? $"{baseUri.Scheme}://{host}"
                : $"{baseUri.Scheme}://{host}:{baseUri.Port}";
        }

        var request = httpContextAccessor.HttpContext?.Request;
        var scheme = request?.Scheme ?? "https";
        var port = request?.Host.Port;

        return port is null or 80 or 443 ? $"{scheme}://{host}" : $"{scheme}://{host}:{port}";
    }

    public string LeadsInbox(Guid siteId) => Absolute($"manage/{siteId}/leads");

    public string OrdersInbox(Guid siteId) => Absolute($"manage/{siteId}/orders");

    public string MagicLink(string token) => Absolute($"auth/verify?token={Uri.EscapeDataString(token)}");
}
