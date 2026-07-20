namespace WebsiteBuilder.Core.Tenancy;

/// <summary>The tenant the current request is acting on behalf of; null outside a tenant scope.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
}

/// <summary>Scoped per request; populated by tenant-resolution middleware (WB-12).</summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; set; }
}
