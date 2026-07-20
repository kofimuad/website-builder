namespace WebsiteBuilder.Core.Tenancy;

public enum HostKind
{
    /// <summary>The platform domain itself, or one of its reserved subdomains: the builder app, not a tenant site.</summary>
    Platform,

    /// <summary>A single-label subdomain of the platform domain that may name a tenant.</summary>
    TenantSubdomain,

    /// <summary>Some other domain entirely: a customer's own domain (custom-domain support lands later).</summary>
    CustomDomain,
}

public readonly record struct HostClassification(HostKind Kind, string? Subdomain)
{
    /// <summary>
    /// Decides what a request's host name means, without touching the database.
    /// <paramref name="host"/> must already have any port stripped (use <c>HttpRequest.Host.Host</c>).
    /// </summary>
    public static HostClassification Classify(string? host, TenantResolutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(host))
        {
            return new HostClassification(HostKind.Platform, null);
        }

        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        var platformDomain = options.PlatformDomain.Trim().TrimEnd('.').ToLowerInvariant();

        if (host == platformDomain)
        {
            return new HostClassification(HostKind.Platform, null);
        }

        if (!host.EndsWith('.' + platformDomain))
        {
            return new HostClassification(HostKind.CustomDomain, null);
        }

        var label = host[..^(platformDomain.Length + 1)];

        // Only a single label maps to a tenant; "a.b.platform.com" is not a tenant site.
        if (label.Contains('.'))
        {
            return new HostClassification(HostKind.CustomDomain, null);
        }

        return options.ReservedSubdomains.Contains(label, StringComparer.OrdinalIgnoreCase)
            ? new HostClassification(HostKind.Platform, null)
            : new HostClassification(HostKind.TenantSubdomain, label);
    }
}
