using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;

namespace WebsiteBuilder.Tests;

/// <summary>Boots the real application pipeline against a throwaway Postgres.</summary>
public sealed class TenantAppFactory(PostgresFixture fixture) : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = fixture.ConnectionString,
            ["TenantResolution:PlatformDomain"] = "platform.com",
        }));

        return base.CreateHost(builder);
    }
}

[Collection(nameof(PostgresCollection))]
public class TenantResolutionMiddlewareTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    private async Task<string> SeedTenantAsync()
    {
        var subdomain = $"t{Guid.NewGuid():N}"[..12];

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
        db.Tenants.Add(new Tenant { Subdomain = subdomain, Name = "Seeded Tenant" });
        await db.SaveChangesAsync();

        return subdomain;
    }

    [Fact]
    public async Task A_known_subdomain_resolves_and_serves_the_site()
    {
        var subdomain = await SeedTenantAsync();
        var client = CreateClient();

        var response = await client.GetAsync($"http://{subdomain}.platform.com/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_subdomain_gets_the_friendly_not_found_page()
    {
        var client = CreateClient();

        var response = await client.GetAsync("http://nobody-here.platform.com/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("no website here yet", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("www")]
    [InlineData("app")]
    [InlineData("api")]
    [InlineData("admin")]
    public async Task Reserved_subdomains_serve_the_builder_rather_than_a_tenant(string reserved)
    {
        var client = CreateClient();

        var response = await client.GetAsync($"http://{reserved}.platform.com/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_platform_apex_serves_the_builder()
    {
        var client = CreateClient();

        var response = await client.GetAsync("http://platform.com/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_health_endpoint_answers_on_a_host_that_is_not_the_platform_domain()
    {
        // Railway probes the host it assigns the service, which is never the platform domain.
        var client = CreateClient();

        var response = await client.GetAsync("http://some-service.up.railway.app/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unmapped_custom_domain_gets_the_not_found_page()
    {
        var client = CreateClient();

        var response = await client.GetAsync("http://someones-own-domain.com/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_resolved_tenant_is_visible_to_tenant_scoped_queries()
    {
        var subdomain = await SeedTenantAsync();

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITenantStore>();
        var tenantId = await store.FindIdBySubdomainAsync(subdomain);

        Assert.NotNull(tenantId);
    }
}
