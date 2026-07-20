namespace WebsiteBuilder.Core.Tenancy;

/// <summary>Marker for entities that belong to a single tenant and must never leak across tenants.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
