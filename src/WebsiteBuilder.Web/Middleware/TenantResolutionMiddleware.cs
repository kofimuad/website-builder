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
                await next(context);
                return;

            default:
                // Custom domains are not mapped to tenants yet (WB-9 publishing).
                await RenderSiteNotFoundAsync(context);
                return;
        }
    }

    private async Task RenderSiteNotFoundAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Request.Path = SiteNotFoundPath;
        await next(context);
    }
}
