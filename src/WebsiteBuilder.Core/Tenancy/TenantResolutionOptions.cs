namespace WebsiteBuilder.Core.Tenancy;

public sealed class TenantResolutionOptions
{
    public const string SectionName = "TenantResolution";

    /// <summary>The domain tenant subdomains hang off, e.g. "platform.com". No leading dot.</summary>
    public string PlatformDomain { get; set; } = "localhost";

    /// <summary>
    /// Subdomains the platform keeps for itself; these never resolve to a tenant.
    /// An array rather than a set because the configuration binder merges into an existing
    /// collection, which would make configured values additive to the defaults.
    /// </summary>
    public string[] ReservedSubdomains { get; set; } = ["www", "app", "api", "admin"];
}
