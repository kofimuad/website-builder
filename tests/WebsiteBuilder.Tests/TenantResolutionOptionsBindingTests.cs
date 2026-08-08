using Microsoft.Extensions.Configuration;
using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Tests;

/// <summary>
/// Pins how <see cref="TenantResolutionOptions.ReservedSubdomains"/> behaves when configuration
/// also supplies a list. The defaults are a security control — every mail label and every name
/// that could impersonate the platform — so it matters a great deal whether configuration adds to
/// them or replaces them, and the answer is not obvious from reading the binder.
/// </summary>
public class TenantResolutionOptionsBindingTests
{
    private static TenantResolutionOptions Bind(params string[] configured)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configured
                .Select((value, index) => new KeyValuePair<string, string?>(
                    $"TenantResolution:ReservedSubdomains:{index}", value)))
            .Build();

        var options = new TenantResolutionOptions { PlatformDomain = "platform.com" };
        config.GetSection(TenantResolutionOptions.SectionName).Bind(options);
        return options;
    }

    [Fact]
    public void Configuring_the_list_does_not_drop_the_security_defaults()
    {
        // appsettings.json historically restated the original four names. If binding replaced the
        // array, that one line would silently un-reserve `send` — breaking mail DNS — and hand out
        // `login` and `secure` to whoever signed up first.
        var options = Bind("www", "app", "api", "admin");

        Assert.Equal(HostKind.Platform, HostClassification.Classify("send.platform.com", options).Kind);
        Assert.Equal(HostKind.Platform, HostClassification.Classify("secure.platform.com", options).Kind);
        Assert.Equal(HostKind.Platform, HostClassification.Classify("login.platform.com", options).Kind);
    }

    [Fact]
    public void A_configured_name_is_reserved_in_addition_to_the_defaults()
    {
        var options = Bind("acme-internal");

        Assert.Equal(HostKind.Platform, HostClassification.Classify("acme-internal.platform.com", options).Kind);
        Assert.Equal(HostKind.Platform, HostClassification.Classify("admin.platform.com", options).Kind);
        Assert.Equal(HostKind.TenantSubdomain, HostClassification.Classify("joes.platform.com", options).Kind);
    }
}
