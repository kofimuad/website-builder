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

    public string LeadsInbox(Guid siteId) => Absolute($"manage/{siteId}/leads");

    public string MagicLink(string token) => Absolute($"auth/verify?token={Uri.EscapeDataString(token)}");
}
