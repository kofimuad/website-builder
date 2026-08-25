using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Shop;

namespace WebsiteBuilder.Tests;

/// <summary>
/// The published home page is output-cached per tenant for five minutes, and products are live
/// rows with no publish step. Those two facts contradicted each other: only publishing evicted the
/// cache, so an owner who changed a price watched the products page say "Saved" while their own
/// website carried on showing the old one, with nothing they could press to force it through.
/// <para>
/// That is the "the price is not showing" report, and these are its regression tests.
/// </para>
/// </summary>
[Collection(nameof(PostgresCollection))]
public class ShopCacheTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private static SiteDefinition WithShop() => new()
    {
        Meta = new SiteMeta { BusinessName = "Auntie Ako's Kitchen" },
        Theme = new SiteTheme(),
        Sections =
        [
            new HeroSection { Headline = "Hot food, fast", Subheadline = "Osu" },
            new ShopSection { Heading = "Our menu", MaxItems = 6 },
        ],
    };

    private async Task<(string Subdomain, Guid TenantId)> SeedAsync(params Product[] products)
    {
        var subdomain = $"c{Guid.NewGuid():N}"[..12];

        using var scope = _factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

        var tenant = new Tenant { Subdomain = subdomain, Name = "Shop Tenant" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        tenantContext.TenantId = tenant.Id;

        var site = new Site { Name = "Site", Draft = WithShop() };
        site.Publish();
        db.Sites.Add(site);

        foreach (var product in products)
        {
            product.TenantId = tenant.Id;
            db.Products.Add(product);
        }

        await db.SaveChangesAsync();

        return (subdomain, tenant.Id);
    }

    /// <summary>Edits the catalog the way the builder's products page does — through the service.</summary>
    private async Task AsOwnerAsync(Guid tenantId, Func<ProductsService, WebsiteBuilderDbContext, Task> work)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenantId;

        await work(
            scope.ServiceProvider.GetRequiredService<ProductsService>(),
            scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>());
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task A_new_price_shows_on_the_home_page_without_waiting_for_the_cache()
    {
        var (subdomain, tenantId) = await SeedAsync(new Product
        {
            Name = "Jollof and chicken",
            Slug = "jollof-and-chicken",
            PriceMinor = 3000,
            Currency = "GHS",
        });

        var client = CreateClient();
        var home = $"http://{subdomain}.platform.com/";

        // Populates the cache, which is what made this bug survive a reload.
        Assert.Contains("GHS 30.00", await client.GetStringAsync(home));

        await AsOwnerAsync(tenantId, async (products, db) =>
        {
            var product = await db.Products.FirstAsync();
            product.PriceMinor = 4500;
            await products.SaveAsync(product);
        });

        var after = await client.GetStringAsync(home);

        Assert.Contains("GHS 45.00", after);
        Assert.DoesNotContain("GHS 30.00", after);
    }

    [Fact]
    public async Task A_first_price_replaces_ask_for_a_price_straight_away()
    {
        // The worst version of the bug: the owner adds the price that was missing, and the site
        // keeps telling customers to ask for one.
        var (subdomain, tenantId) = await SeedAsync(new Product
        {
            Name = "Waakye",
            Slug = "waakye",
            PriceMinor = null,
            Currency = "GHS",
        });

        var client = CreateClient();
        var home = $"http://{subdomain}.platform.com/";

        Assert.Contains("Ask for a price", await client.GetStringAsync(home));

        await AsOwnerAsync(tenantId, async (products, db) =>
        {
            var product = await db.Products.FirstAsync();
            product.PriceMinor = 2500;
            await products.SaveAsync(product);
        });

        var after = await client.GetStringAsync(home);

        Assert.Contains("GHS 25.00", after);
        Assert.DoesNotContain("Ask for a price", after);
    }

    [Fact]
    public async Task A_newly_added_product_appears_on_the_home_page()
    {
        var (subdomain, tenantId) = await SeedAsync();

        var client = CreateClient();
        var home = $"http://{subdomain}.platform.com/";

        Assert.Contains("Nothing in the shop yet", await client.GetStringAsync(home));

        await AsOwnerAsync(tenantId, async (products, _) => await products.AddAsync("Kelewele"));

        Assert.Contains("Kelewele", await client.GetStringAsync(home));
    }

    [Fact]
    public async Task A_deleted_product_disappears_from_the_home_page()
    {
        // The mirror of the above, and the one with a real cost: a product left on a cached page
        // is one a customer can still order.
        var (subdomain, tenantId) = await SeedAsync(new Product
        {
            Name = "Kelewele",
            Slug = "kelewele",
            PriceMinor = 1000,
            Currency = "GHS",
        });

        var client = CreateClient();
        var home = $"http://{subdomain}.platform.com/";

        Assert.Contains("Kelewele", await client.GetStringAsync(home));

        await AsOwnerAsync(tenantId, async (products, db) =>
        {
            var product = await db.Products.FirstAsync();
            await products.DeleteAsync(product.Id);
        });

        Assert.DoesNotContain("Kelewele", await client.GetStringAsync(home));
    }

    [Fact]
    public async Task One_tenants_catalog_edit_does_not_evict_another_tenants_page()
    {
        // Eviction is by tenant tag. A blunt "clear everything" would work for correctness and
        // quietly throw away every other business's cached page on every keystroke.
        var (mine, myTenantId) = await SeedAsync(new Product
        {
            Name = "Jollof", Slug = "jollof", PriceMinor = 3000, Currency = "GHS",
        });
        var (theirs, _) = await SeedAsync(new Product
        {
            Name = "Banku", Slug = "banku", PriceMinor = 2000, Currency = "GHS",
        });

        var client = CreateClient();

        Assert.Contains("GHS 30.00", await client.GetStringAsync($"http://{mine}.platform.com/"));
        Assert.Contains("GHS 20.00", await client.GetStringAsync($"http://{theirs}.platform.com/"));

        await AsOwnerAsync(myTenantId, async (products, db) =>
        {
            var product = await db.Products.FirstAsync();
            product.PriceMinor = 3300;
            await products.SaveAsync(product);
        });

        Assert.Contains("GHS 33.00", await client.GetStringAsync($"http://{mine}.platform.com/"));
        Assert.Contains("GHS 20.00", await client.GetStringAsync($"http://{theirs}.platform.com/"));
    }
}
