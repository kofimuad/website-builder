using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Web.Platform;

namespace WebsiteBuilder.Tests;

/// <summary>
/// The address a tenant's site lives at. This is the string an owner copies onto a card or sends
/// to a customer, so getting the scheme or port wrong is not cosmetic — the link simply fails.
/// </summary>
public class PlatformUrlsTests
{
    private static PlatformUrls Build(string? publicBaseUrl, string platformDomain = "csbuild.app", HttpContext? context = null)
    {
        var accessor = new HttpContextAccessor { HttpContext = context };

        return new PlatformUrls(
            accessor,
            Options.Create(new PlatformOptions { PublicBaseUrl = publicBaseUrl }),
            Options.Create(new TenantResolutionOptions { PlatformDomain = platformDomain }));
    }

    [Fact]
    public void A_tenant_site_hangs_off_the_platform_domain()
    {
        Assert.Equal("https://joesplumbing.csbuild.app", Build("https://csbuild.app").TenantSite("joesplumbing"));
    }

    [Fact]
    public void The_local_port_survives_so_the_link_works_in_development()
    {
        // Without this the editor would offer a link to https://joesplumbing.localhost, which
        // resolves but is not where the app is listening.
        Assert.Equal(
            "http://joesplumbing.localhost:5184",
            Build("http://localhost:5184", "localhost").TenantSite("joesplumbing"));
    }

    [Fact]
    public void The_configured_base_url_wins_over_the_current_request()
    {
        // Called from a Blazor circuit there is no request at all, and behind a proxy the request
        // scheme can be http even though the public address is https. Configuration is the truth.
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("internal.railway.internal", 8080);

        Assert.Equal(
            "https://acme.csbuild.app",
            Build("https://csbuild.app", context: context).TenantSite("acme"));
    }

    [Fact]
    public void With_no_configuration_and_no_request_it_still_produces_an_https_link()
    {
        Assert.Equal("https://acme.csbuild.app", Build(publicBaseUrl: null).TenantSite("acme"));
    }

    [Fact]
    public void A_default_port_is_not_written_into_the_link()
    {
        Assert.Equal("https://acme.csbuild.app", Build("https://csbuild.app:443").TenantSite("acme"));
    }
}
