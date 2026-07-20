using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Tests;

public class HostClassificationTests
{
    private static readonly TenantResolutionOptions Options = new() { PlatformDomain = "platform.com" };

    [Theory]
    [InlineData("platform.com")]
    [InlineData("PLATFORM.COM")]
    [InlineData("platform.com.")]
    [InlineData("www.platform.com")]
    [InlineData("app.platform.com")]
    [InlineData("api.platform.com")]
    [InlineData("admin.platform.com")]
    [InlineData("ADMIN.platform.com")]
    public void Platform_and_reserved_hosts_do_not_resolve_to_a_tenant(string host)
    {
        var result = HostClassification.Classify(host, Options);

        Assert.Equal(HostKind.Platform, result.Kind);
        Assert.Null(result.Subdomain);
    }

    [Theory]
    [InlineData("acme.platform.com", "acme")]
    [InlineData("ACME.platform.com", "acme")]
    [InlineData("joes-plumbing.platform.com", "joes-plumbing")]
    public void A_single_label_subdomain_names_a_tenant(string host, string expected)
    {
        var result = HostClassification.Classify(host, Options);

        Assert.Equal(HostKind.TenantSubdomain, result.Kind);
        Assert.Equal(expected, result.Subdomain);
    }

    [Theory]
    [InlineData("acme.example.com")]
    [InlineData("example.com")]
    [InlineData("notplatform.com")]
    public void An_unrelated_domain_is_a_custom_domain(string host)
    {
        Assert.Equal(HostKind.CustomDomain, HostClassification.Classify(host, Options).Kind);
    }

    [Fact]
    public void A_multi_label_subdomain_is_not_a_tenant()
    {
        Assert.Equal(HostKind.CustomDomain, HostClassification.Classify("a.b.platform.com", Options).Kind);
    }

    [Fact]
    public void A_domain_merely_ending_in_the_platform_name_is_not_a_subdomain()
    {
        // "evilplatform.com" ends with "platform.com" as a string but is a different domain.
        Assert.Equal(HostKind.CustomDomain, HostClassification.Classify("evilplatform.com", Options).Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_host_falls_back_to_the_platform(string? host)
    {
        Assert.Equal(HostKind.Platform, HostClassification.Classify(host, Options).Kind);
    }

    [Fact]
    public void Configured_reserved_subdomains_replace_the_defaults()
    {
        var options = new TenantResolutionOptions
        {
            PlatformDomain = "platform.com",
            ReservedSubdomains = ["status"],
        };

        Assert.Equal(HostKind.Platform, HostClassification.Classify("status.platform.com", options).Kind);
        Assert.Equal(HostKind.TenantSubdomain, HostClassification.Classify("admin.platform.com", options).Kind);
    }
}
