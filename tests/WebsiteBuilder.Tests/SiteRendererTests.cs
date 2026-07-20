using System.Net;
using Microsoft.Extensions.DependencyInjection;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Publishing;

namespace WebsiteBuilder.Tests;

[Collection(nameof(PostgresCollection))]
public class SiteRendererTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private static SiteDefinition SampleDefinition() => new()
    {
        Meta = new SiteMeta
        {
            BusinessName = "Joe's Plumbing",
            SeoTitle = "Joe's Plumbing — Accra",
            SeoDescription = "Emergency plumbing across Accra, same day.",
        },
        Theme = new SiteTheme { Palette = new ColorPalette { Primary = "#0a7d55" } },
        Sections =
        [
            new HeroSection { Headline = "Blocked drain?", Subheadline = "We come today" },
            new ServicesSection
            {
                Heading = "What we do",
                Items = [new ServiceItem { Title = "Drain clearing", Description = "Cleared fast" }],
            },
            new ContactSection { Heading = "Get in touch", PhoneNumber = "+233200000000", Email = "joe@example.com" },
            new CtaSection { Headline = "Ready?", ButtonLabel = "Book now", ButtonUrl = "/contact" },
        ],
    };

    /// <summary>Creates a tenant with one site, published unless told otherwise.</summary>
    private async Task<(string Subdomain, Guid SiteId, Guid TenantId)> SeedSiteAsync(bool publish = true, SiteDefinition? definition = null)
    {
        var subdomain = $"r{Guid.NewGuid():N}"[..12];

        using var scope = _factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

        var tenant = new Tenant { Subdomain = subdomain, Name = "Render Tenant" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        tenantContext.TenantId = tenant.Id;
        var site = new Site { Name = "Site", Draft = definition ?? SampleDefinition() };
        if (publish)
        {
            site.Publish();
        }

        db.Sites.Add(site);
        await db.SaveChangesAsync();

        return (subdomain, site.Id, tenant.Id);
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    /// <summary>
    /// Razor encodes characters such as '+' as numeric entities, which browsers decode. Assert
    /// against the decoded text so the tests describe what a visitor sees.
    /// </summary>
    private static string Decode(string html) => WebUtility.HtmlDecode(html);

    [Fact]
    public async Task A_published_site_renders_its_content()
    {
        var (subdomain, _, _) = await SeedSiteAsync();

        var response = await CreateClient().GetAsync($"http://{subdomain}.platform.com/");
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Blocked drain?", html);
        Assert.Contains("Drain clearing", html);
        Assert.Contains("Book now", html);
        Assert.Contains("tel:+233200000000", html);
    }

    [Fact]
    public async Task The_rendered_page_carries_seo_metadata_and_a_mobile_viewport()
    {
        var (subdomain, _, _) = await SeedSiteAsync();

        var html = Decode(await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/"));

        Assert.Contains("<title>Joe's Plumbing — Accra</title>", html);
        Assert.Contains("Emergency plumbing across Accra", html);
        Assert.Contains("width=device-width", html);
    }

    [Fact]
    public async Task Non_ascii_text_is_emitted_as_utf8_rather_than_numeric_entities()
    {
        var definition = SampleDefinition();
        definition.Meta.SeoTitle = "Café Ámà — Accra";

        var (subdomain, _, _) = await SeedSiteAsync(definition: definition);

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/");

        Assert.Contains("Café Ámà — Accra", html);
        Assert.DoesNotContain("&#x2014;", html);
    }

    [Fact]
    public async Task The_theme_drives_the_rendered_styles()
    {
        var (subdomain, _, _) = await SeedSiteAsync();

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/");

        Assert.Contains("--primary: #0a7d55", html);
    }

    [Fact]
    public async Task Published_sites_carry_no_javascript()
    {
        var (subdomain, _, _) = await SeedSiteAsync();

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blazor", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hidden_sections_are_not_rendered()
    {
        var definition = SampleDefinition();
        definition.Sections[0].Visible = false;

        var (subdomain, _, _) = await SeedSiteAsync(definition: definition);

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/");

        Assert.DoesNotContain("Blocked drain?", html);
        Assert.Contains("Drain clearing", html);
    }

    [Fact]
    public async Task A_site_with_nothing_published_is_not_served()
    {
        var (subdomain, _, _) = await SeedSiteAsync(publish: false);

        var response = await CreateClient().GetAsync($"http://{subdomain}.platform.com/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("no website here yet", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Draft_edits_are_never_visible_to_visitors()
    {
        var (subdomain, siteId, tenantId) = await SeedSiteAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            // The tenant must be in scope before querying, or the filter hides the row.
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenantId;
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            var site = await db.Sites.FindAsync(siteId);

            ((HeroSection)site!.Draft.Sections[0]).Headline = "Unpublished draft headline";
            await db.SaveChangesAsync();
        }

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/");

        Assert.DoesNotContain("Unpublished draft headline", html);
        Assert.Contains("Blocked drain?", html);
    }

    [Fact]
    public async Task Unknown_paths_on_a_tenant_host_do_not_reach_builder_pages()
    {
        var (subdomain, _, _) = await SeedSiteAsync();

        var response = await CreateClient().GetAsync($"http://{subdomain}.platform.com/some-admin-page");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

[Collection(nameof(PostgresCollection))]
public class SiteOutputCacheTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private async Task<(string Subdomain, Guid SiteId, Guid TenantId)> SeedPublishedSiteAsync(string headline)
    {
        var subdomain = $"c{Guid.NewGuid():N}"[..12];

        using var scope = _factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

        var tenant = new Tenant { Subdomain = subdomain, Name = "Cache Tenant" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        tenantContext.TenantId = tenant.Id;
        var site = new Site
        {
            Name = "Site",
            Draft = new SiteDefinition { Sections = [new HeroSection { Headline = headline }] },
        };
        site.Publish();
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        return (subdomain, site.Id, tenant.Id);
    }

    private async Task EditDraftAsync(Guid siteId, Guid tenantId, string headline)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenantId;
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

        var site = await db.Sites.FindAsync(siteId);
        ((HeroSection)site!.Draft.Sections[0]).Headline = headline;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_second_visit_is_served_from_the_cache()
    {
        var (subdomain, siteId, tenantId) = await SeedPublishedSiteAsync("First version");
        var client = _factory.CreateClient();
        var url = $"http://{subdomain}.platform.com/";

        Assert.Contains("First version", await client.GetStringAsync(url));

        // Publish a change behind the cache's back: the cached copy must still be served.
        await EditDraftAsync(siteId, tenantId, "Second version");
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenantId;
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            var site = await db.Sites.FindAsync(siteId);
            site!.Publish();
            await db.SaveChangesAsync();
        }

        Assert.Contains("First version", await client.GetStringAsync(url));
    }

    [Fact]
    public async Task Publishing_through_the_publisher_evicts_the_cached_site()
    {
        var (subdomain, siteId, tenantId) = await SeedPublishedSiteAsync("First version");
        var client = _factory.CreateClient();
        var url = $"http://{subdomain}.platform.com/";

        Assert.Contains("First version", await client.GetStringAsync(url));

        await EditDraftAsync(siteId, tenantId, "Second version");

        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenantId;
            await scope.ServiceProvider.GetRequiredService<SitePublisher>().PublishAsync(siteId);
        }

        Assert.Contains("Second version", await client.GetStringAsync(url));
    }

    [Fact]
    public async Task Publishing_one_tenant_does_not_evict_another()
    {
        var first = await SeedPublishedSiteAsync("Tenant one");
        var second = await SeedPublishedSiteAsync("Tenant two");
        var client = _factory.CreateClient();

        await client.GetStringAsync($"http://{first.Subdomain}.platform.com/");
        await client.GetStringAsync($"http://{second.Subdomain}.platform.com/");

        await EditDraftAsync(second.SiteId, second.TenantId, "Tenant two updated");
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = second.TenantId;
            await scope.ServiceProvider.GetRequiredService<SitePublisher>().PublishAsync(second.SiteId);
        }

        Assert.Contains("Tenant one", await client.GetStringAsync($"http://{first.Subdomain}.platform.com/"));
        Assert.Contains("Tenant two updated", await client.GetStringAsync($"http://{second.Subdomain}.platform.com/"));
    }

    [Fact]
    public async Task One_tenants_cached_page_is_never_served_to_another()
    {
        var first = await SeedPublishedSiteAsync("Tenant one");
        var second = await SeedPublishedSiteAsync("Tenant two");
        var client = _factory.CreateClient();

        await client.GetStringAsync($"http://{first.Subdomain}.platform.com/");
        var otherHtml = await client.GetStringAsync($"http://{second.Subdomain}.platform.com/");

        Assert.Contains("Tenant two", otherHtml);
        Assert.DoesNotContain("Tenant one", otherHtml);
    }

    [Fact]
    public async Task A_site_that_becomes_published_is_served_without_waiting_for_the_cache_to_expire()
    {
        // The 404 for "nothing published yet" must not be cached, or a newly published site
        // would stay invisible until the entry expired.
        var subdomain = $"c{Guid.NewGuid():N}"[..12];
        Guid tenantId, siteId;

        using (var scope = _factory.Services.CreateScope())
        {
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            var tenant = new Tenant { Subdomain = subdomain, Name = "Later Tenant" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            tenantId = tenant.Id;
            tenantContext.TenantId = tenantId;
            var site = new Site
            {
                Name = "Site",
                Draft = new SiteDefinition { Sections = [new HeroSection { Headline = "Now live" }] },
            };
            db.Sites.Add(site);
            await db.SaveChangesAsync();
            siteId = site.Id;
        }

        var client = _factory.CreateClient();
        var url = $"http://{subdomain}.platform.com/";

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenantId;
            await scope.ServiceProvider.GetRequiredService<SitePublisher>().PublishAsync(siteId);
        }

        Assert.Contains("Now live", await client.GetStringAsync(url));
    }
}
