using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Core.Entities;

/// <summary>
/// Something the business sells.
/// <para>
/// Relational rather than part of the site's jsonb definition, and the reasons are worth keeping:
/// a draft that gets published would overwrite whatever the catalog had become since; stock — when
/// it arrives — is written by customers buying, not by the owner editing; and an order needs a
/// foreign key to point at. A product is therefore <b>live the moment it is saved</b>. There is no
/// draft copy of a price.
/// </para>
/// <para>
/// The site definition carries only a <c>shop</c> section saying where the catalog appears. That
/// keeps the document a document.
/// </para>
/// </summary>
public class Product : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>The address this product lives at, unique within the tenant: /products/{slug}.</summary>
    public string Slug { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>
    /// Price in the currency's smallest unit — pesewas, not cedis. Money in a floating-point type
    /// is a rounding bug waiting for a busy Saturday, and a nullable price means "ask us", which
    /// is a real way to sell here.
    /// </summary>
    public long? PriceMinor { get; set; }

    /// <summary>ISO 4217. Ghana first, but a business that prices in dollars must not be forced into cedis.</summary>
    public string Currency { get; set; } = "GHS";

    public string? ImageUrl { get; set; }

    /// <summary>Hidden from the shop without being deleted — the usual reason is "sold out for now".</summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>The owner's chosen order. Ties break on name so the catalog never shuffles between page loads.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Formats the price the way a customer reads it, or null when there is none to show.</summary>
    public string? DisplayPrice() => PriceMinor is null
        ? null
        : $"{Currency} {PriceMinor.Value / 100m:N2}";
}
