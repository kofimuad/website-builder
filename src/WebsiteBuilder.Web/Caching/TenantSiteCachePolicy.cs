using Microsoft.AspNetCore.OutputCaching;
using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Web.Caching;

/// <summary>
/// Caches published sites per tenant. Entries are tagged with the tenant so publishing can evict
/// exactly one site's pages — see <see cref="TagFor"/>.
/// </summary>
public sealed class TenantSiteCachePolicy : IOutputCachePolicy
{
    public const string Name = "TenantSite";

    public static string TagFor(Guid tenantId) => $"tenant:{tenantId}";

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var tenantId = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>().TenantId;

        // With no tenant resolved there is no published site to serve, and caching the response
        // would risk showing it under a different host later.
        if (tenantId is null)
        {
            context.EnableOutputCaching = false;
            return ValueTask.CompletedTask;
        }

        context.EnableOutputCaching = true;
        context.AllowCacheLookup = true;
        context.AllowCacheStorage = true;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = TimeSpan.FromMinutes(5);

        // Keyed explicitly on the tenant rather than the host, so a tenant reached by more than
        // one host name still shares one entry and one eviction.
        context.CacheVaryByRules.VaryByValues["tenant"] = tenantId.Value.ToString();
        context.Tags.Add(TagFor(tenantId.Value));

        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;

        // Never cache the "nothing published yet" 404: it stops being true the moment the owner
        // publishes, and publishing a site that has never been live has nothing to evict.
        if (response.StatusCode != StatusCodes.Status200OK)
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }
}
