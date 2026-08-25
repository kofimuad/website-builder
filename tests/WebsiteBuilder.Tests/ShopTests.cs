using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Shop;

namespace WebsiteBuilder.Tests;

/// <summary>
/// The shop as a visitor meets it, through the real pipeline: tenant resolution, the published
/// site, the catalog and the cart cookie.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class ShopTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private static SiteDefinition WithShop(bool shop = true)
    {
        var sections = new List<SiteSection>
        {
            new HeroSection { Headline = "Hot food, fast", Subheadline = "Osu" },
            new ContactSection
            {
                Heading = "Get in touch",
                PhoneNumber = "+233200000000",
                WhatsAppNumber = "+233200000000",
            },
        };

        if (shop)
        {
            sections.Insert(1, new ShopSection { Heading = "Our menu", MaxItems = 6 });
        }

        return new SiteDefinition
        {
            Meta = new SiteMeta { BusinessName = "Auntie Ako's Kitchen" },
            Theme = new SiteTheme(),
            Sections = sections,
        };
    }

    private async Task<string> SeedShopAsync(bool withShopSection = true, params Product[] products)
    {
        var subdomain = $"s{Guid.NewGuid():N}"[..12];

        using var scope = _factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

        var tenant = new Tenant { Subdomain = subdomain, Name = "Shop Tenant" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        tenantContext.TenantId = tenant.Id;

        var site = new Site { Name = "Site", Draft = WithShop(withShopSection) };
        site.Publish();
        db.Sites.Add(site);

        foreach (var product in products)
        {
            product.TenantId = tenant.Id;
            db.Products.Add(product);
        }

        await db.SaveChangesAsync();

        return subdomain;
    }

    private static Product Jollof(bool available = true) => new()
    {
        Name = "Jollof and chicken",
        Slug = "jollof-and-chicken",
        Description = "Smoky, cooked to order.",
        PriceMinor = 3000,
        Currency = "GHS",
        IsAvailable = available,
    };

    // Redirects are not followed: the add-to-cart post returns one, and its Set-Cookie header is
    // the thing worth asserting on.
    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task The_shop_page_lists_available_products()
    {
        var subdomain = await SeedShopAsync(products: Jollof());

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/shop");

        Assert.Contains("Jollof and chicken", html);
        Assert.Contains("GHS 30.00", html);
    }

    [Fact]
    public async Task A_tenant_with_no_shop_section_does_not_serve_shop_pages()
    {
        // The section is what says this business sells online. Without it the URL should be as
        // absent as any other page we do not serve.
        var subdomain = await SeedShopAsync(withShopSection: false, products: Jollof());
        var client = CreateClient();

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"http://{subdomain}.platform.com/shop")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"http://{subdomain}.platform.com/products/jollof-and-chicken")).StatusCode);
    }

    [Fact]
    public async Task An_unavailable_product_is_not_found_rather_than_hidden()
    {
        // Its page may be in a customer's WhatsApp history; it must not still take an order.
        var subdomain = await SeedShopAsync(products: Jollof(available: false));
        var client = CreateClient();

        var shop = await client.GetStringAsync($"http://{subdomain}.platform.com/shop");
        Assert.DoesNotContain("Jollof and chicken", shop);

        var product = await client.GetAsync($"http://{subdomain}.platform.com/products/jollof-and-chicken");
        Assert.Equal(HttpStatusCode.NotFound, product.StatusCode);
    }

    [Fact]
    public async Task Builder_pages_are_still_refused_on_a_tenant_host()
    {
        // The allowlist exists so a dashboard can never appear on a customer's own domain.
        var subdomain = await SeedShopAsync(products: Jollof());
        var client = CreateClient();

        foreach (var path in new[] { "/dashboard", "/start", "/shop-admin", "/manage/x/products" })
        {
            var response = await client.GetAsync($"http://{subdomain}.platform.com{path}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task Adding_to_the_cart_and_ordering_composes_a_whatsapp_message()
    {
        var subdomain = await SeedShopAsync(products: Jollof());
        var client = CreateClient();
        var host = $"http://{subdomain}.platform.com";

        var added = await client.PostAsync(
            $"{host}/products/jollof-and-chicken",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("quantity", "2")]));

        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);

        var cookie = Assert.Single(added.Headers.GetValues("Set-Cookie"));
        var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/cart");
        request.Headers.Add("Cookie", cookie.Split(';')[0]);

        var cart = WebUtility.HtmlDecode(await (await client.SendAsync(request)).Content.ReadAsStringAsync());

        Assert.Contains("Jollof and chicken", cart);
        Assert.Contains("GHS 60.00", cart);
        Assert.Contains("https://wa.me/233200000000?text=", cart);
    }

    [Fact]
    public async Task A_cart_holding_a_withdrawn_product_empties_rather_than_breaking()
    {
        var subdomain = await SeedShopAsync(products: Jollof());
        var host = $"http://{subdomain}.platform.com";

        // A product id that is not in this catalog at all — the state a cookie reaches after the
        // owner deletes something.
        var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/cart");
        request.Headers.Add("Cookie", $"{Cart.CookieName}={Guid.NewGuid():N}:2");

        var response = await CreateClient().SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Your order is empty", html);
    }

    [Fact]
    public async Task One_tenants_product_is_never_reachable_from_another_tenants_host()
    {
        var mine = await SeedShopAsync(products: Jollof());
        var theirs = await SeedShopAsync();

        var client = CreateClient();

        Assert.Contains("Jollof", await client.GetStringAsync($"http://{mine}.platform.com/shop"));

        var crossed = await client.GetAsync($"http://{theirs}.platform.com/products/jollof-and-chicken");
        Assert.Equal(HttpStatusCode.NotFound, crossed.StatusCode);
    }

    [Fact]
    public async Task Nav_links_on_a_shop_page_point_back_at_the_home_page()
    {
        // The anchors live on the home page, so from /cart they have to be "/#…". Bare "#…" would
        // scroll the cart page to nothing.
        var subdomain = await SeedShopAsync(products: Jollof());

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/cart");

        Assert.Contains("href=\"/#contact\"", html);
    }

    [Fact]
    public async Task The_home_page_shows_the_shop_section_with_its_products()
    {
        var subdomain = await SeedShopAsync(products: Jollof());

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/");

        Assert.Contains("Our menu", html);
        Assert.Contains("Jollof and chicken", html);
        Assert.Contains("/products/jollof-and-chicken", html);
    }

    [Fact]
    public async Task Every_page_of_a_shop_site_offers_a_way_back_to_the_basket()
    {
        // There was none. A customer who added something could only reach the cart from the
        // product page they added it on, so browsing away from that page lost the order.
        var subdomain = await SeedShopAsync(products: Jollof());
        var client = CreateClient();
        var host = $"http://{subdomain}.platform.com";

        foreach (var path in new[] { "/", "/shop", "/products/jollof-and-chicken", "/cart" })
        {
            Assert.Contains("class=\"topbar-cart\"", await client.GetStringAsync($"{host}{path}"));
        }
    }

    [Fact]
    public async Task A_site_with_no_shop_has_no_basket_link()
    {
        var subdomain = await SeedShopAsync(withShopSection: false);

        Assert.DoesNotContain("class=\"topbar-cart\"", await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/"));
    }

    [Fact]
    public async Task The_basket_count_never_reaches_the_cached_home_page()
    {
        // The home page is output-cached per tenant, so a count rendered into it would be one
        // visitor's basket served to every visitor. The shop pages are uncached and do show it.
        var subdomain = await SeedShopAsync(products: Jollof());
        var client = CreateClient();
        var host = $"http://{subdomain}.platform.com";

        var added = await client.PostAsync(
            $"{host}/products/jollof-and-chicken",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("quantity", "3")]));

        var cookie = Assert.Single(added.Headers.GetValues("Set-Cookie")).Split(';')[0];

        var shop = new HttpRequestMessage(HttpMethod.Get, $"{host}/shop");
        shop.Headers.Add("Cookie", cookie);
        Assert.Contains("class=\"topbar-cart-count\"", await (await client.SendAsync(shop)).Content.ReadAsStringAsync());

        var home = new HttpRequestMessage(HttpMethod.Get, $"{host}/");
        home.Headers.Add("Cookie", cookie);
        var homeHtml = await (await client.SendAsync(home)).Content.ReadAsStringAsync();

        Assert.Contains("class=\"topbar-cart\"", homeHtml);
        Assert.DoesNotContain("class=\"topbar-cart-count\"", homeHtml);
    }

    [Fact]
    public async Task The_shop_page_carries_the_owners_own_heading()
    {
        // It said "Shop" regardless. A business that renamed the section had it renamed everywhere
        // except the page its own "See everything" button leads to.
        var subdomain = await SeedShopAsync(products: Jollof());

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/shop");

        Assert.Contains("Our menu", html);
    }
}
