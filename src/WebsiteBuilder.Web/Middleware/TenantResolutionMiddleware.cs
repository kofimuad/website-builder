using Microsoft.Extensions.Options;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;

namespace WebsiteBuilder.Web.Middleware;

/// <summary>
/// Maps the request's Host header to a tenant and publishes it on the request-scoped
/// <see cref="TenantContext"/>. Must run before routing so the not-found rewrite is picked up.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, IOptions<TenantResolutionOptions> options)
{
    public const string SiteNotFoundPath = "/site-not-found";
    public const string SitePath = "/site";

    /// <summary>
    /// The only paths a tenant host serves besides <c>/</c>.
    /// <para>
    /// An allowlist rather than a blocklist, and it stays one. The rule this enforces is that no
    /// builder page can ever appear on a customer's own domain — a dashboard reachable at
    /// joesplumbing.csbuild.app is both a leak and a phishing surface. Adding a public page to a
    /// tenant site means adding it here deliberately, which is the point.
    /// </para>
    /// </summary>
    private static readonly string[] PublicPaths = ["/shop", "/products", "/cart"];

    private readonly TenantResolutionOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext, ITenantStore tenantStore)
    {
        // Platform infrastructure probes hit the host Railway assigns, not the platform domain,
        // so they must never be treated as a tenant lookup.
        if (context.Request.Path.StartsWithSegments("/healthz"))
        {
            await next(context);
            return;
        }

        var classification = HostClassification.Classify(context.Request.Host.Host, _options);

        switch (classification.Kind)
        {
            case HostKind.Platform:
                await next(context);
                return;

            case HostKind.TenantSubdomain:
                var tenantId = await tenantStore.FindIdBySubdomainAsync(
                    classification.Subdomain!, context.RequestAborted);

                if (tenantId is null)
                {
                    await RenderSiteNotFoundAsync(context);
                    return;
                }

                tenantContext.TenantId = tenantId;

                // A tenant host serves that tenant's published site, its shop, and nothing else.
                // Requests for files (images, fonts, css) fall through to static assets; anything
                // outside the allowlist would otherwise reach builder pages on a customer's domain.
                if (context.Request.Path == "/")
                {
                    context.Request.Path = SitePath;
                }
                else if (!IsPublic(context.Request.Path) && !Path.HasExtension(context.Request.Path.Value))
                {
                    await RenderSiteNotFoundAsync(context);
                    return;
                }

                await next(context);
                return;

            default:
                // Custom domains are not mapped to tenants yet (WB-9 publishing).
                await RenderSiteNotFoundAsync(context);
                return;
        }
    }

    /// <summary>
    /// Matches on whole segments, so <c>/shop</c> and <c>/products/jollof</c> pass while
    /// <c>/shop-admin</c> does not.
    /// </summary>
    private static bool IsPublic(PathString path) =>
        PublicPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    private async Task RenderSiteNotFoundAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Request.Path = SiteNotFoundPath;
        await next(context);
    }
}
