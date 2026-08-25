using System.Net;
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
/// Placing an order, through the real pipeline: tenant resolution, the cart cookie, the checkout
/// post, and the record it leaves behind.
/// <para>
/// Ecommerce v1 ended at a WhatsApp link and stored nothing, so there was nothing here to test.
/// These cover the part that makes an order a record rather than a message.
/// </para>
/// </summary>
[Collection(nameof(PostgresCollection))]
public class OrderTests(PostgresFixture fixture) : IDisposable
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
            new ContactSection
            {
                Heading = "Get in touch",
                PhoneNumber = "+233200000000",
                WhatsAppNumber = "+233200000000",
            },
        ],
    };

    private async Task<(string Subdomain, Guid TenantId)> SeedShopAsync(params Product[] products)
    {
        var subdomain = $"s{Guid.NewGuid():N}"[..12];

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

    private static Product Jollof() => new()
    {
        Name = "Jollof and chicken",
        Slug = "jollof-and-chicken",
        Description = "Smoky, cooked to order.",
        PriceMinor = 3000,
        Currency = "GHS",
    };

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Adds a product to a cart and returns the cookie holding it.</summary>
    private async Task<string> AddToCartAsync(HttpClient client, string host, string slug, int quantity)
    {
        var added = await client.PostAsync(
            $"{host}/products/{slug}",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("quantity", quantity.ToString())]));

        return Assert.Single(added.Headers.GetValues("Set-Cookie")).Split(';')[0];
    }

    private static HttpRequestMessage WithCookie(HttpMethod method, string url, string cookie, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private static FormUrlEncodedContent CheckoutForm(
        string? name = "Ama Mensah", string? phone = "+233244000111", string? note = null, string? honeypot = null) =>
        new([
            new KeyValuePair<string, string>("customerName", name ?? ""),
            new KeyValuePair<string, string>("phoneNumber", phone ?? ""),
            new KeyValuePair<string, string>("note", note ?? ""),
            new KeyValuePair<string, string>("website", honeypot ?? ""),
        ]);

    private async Task<List<Order>> OrdersForAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenantId;
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

        return await db.Orders.AsNoTracking().ToListAsync();
    }

    [Fact]
    public async Task Placing_an_order_records_it_and_confirms_with_a_reference()
    {
        var (subdomain, tenantId) = await SeedShopAsync(Jollof());
        var client = CreateClient();
        var host = $"http://{subdomain}.platform.com";

        var cookie = await AddToCartAsync(client, host, "jollof-and-chicken", 2);

        var placed = await client.SendAsync(
            WithCookie(HttpMethod.Post, $"{host}/checkout", cookie, CheckoutForm(note: "Gate 4, blue door")));

        // Redirect after post, so a refresh cannot place the order twice.
        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        var location = placed.Headers.Location!.ToString();
        Assert.StartsWith("/order/", location);

        var order = Assert.Single(await OrdersForAsync(tenantId));
        Assert.Equal("Ama Mensah", order.CustomerName);
        Assert.Equal("+233244000111", order.PhoneNumber);
        Assert.Equal("Gate 4, blue door", order.Note);
        Assert.Equal(OrderStatus.New, order.Status);
        Assert.False(order.IsRead);
        Assert.Equal(6000, order.TotalMinor);
        Assert.Equal("GHS", order.Currency);

        var line = Assert.Single(order.Items);
        Assert.Equal("Jollof and chicken", line.ProductName);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(3000, line.UnitPriceMinor);

        // And the customer can read it back at the address they were sent to.
        var confirmation = await client.GetStringAsync($"{host}{location}");
        Assert.Contains(order.Reference, confirmation);
        Assert.Contains("Jollof and chicken", confirmation);
    }

    [Fact]
    public async Task An_order_keeps_the_price_the_customer_agreed_to()
    {
        // The reason a line copies the name and price instead of pointing at the product: the owner
        // can re-price a live product minutes later, and an order that re-priced itself with them
        // would be a record of nothing.
        var (subdomain, tenantId) = await SeedShopAsync(Jollof());
        var client = CreateClient();
        var host = $"http://{subdomain}.platform.com";

        var cookie = await AddToCartAsync(client, host, "jollof-and-chicken", 1);
        await client.SendAsync(WithCookie(HttpMethod.Post, $"{host}/checkout", cookie, CheckoutForm()));

        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenantId;
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            var product = await db.Products.FirstAsync();
            product.PriceMinor = 9900;
            product.Name = "Jollof and chicken (large)";
            await db.SaveChangesAsync();
        }

        var order = Assert.Single(await OrdersForAsync(tenantId));

        Assert.Equal(3000, order.TotalMinor);
        Assert.Equal(3000, Assert.Single(order.Items).UnitPriceMinor);
        Assert.Equal("Jollof and chicken", Assert.Single(order.Items).ProductName);
    }

    [Fact]
    public async Task Ordering_a_single_product_goes_straight_to_the_checkout()
    {
        // "Order now" on a product page is the common case, and routing it via the cart was the
        // step customers were getting lost on.
        var (subdomain, _) = await SeedShopAsync(Jollof());
        var host = $"http://{subdomain}.platform.com";

        var response = await CreateClient().PostAsync(
            $"{host}/products/jollof-and-chicken",
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("quantity", "1"),
                new KeyValuePair<string, string>("orderNow", "1"),
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/checkout", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task An_order_with_no_way_to_reply_is_refused()
    {
        // Same rule as a lead: the owner fulfils this by contacting the customer.
        var (subdomain, tenantId) = await SeedShopAsync(Jollof());
        var client = CreateClient();
        var host = $"http://{subdomain}.platform.com";

        var cookie = await AddToCartAsync(client, host, "jollof-and-chicken", 1);

        var response = await client.SendAsync(WithCookie(
            HttpMethod.Post, $"{host}/checkout", cookie, CheckoutForm(phone: null)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("phone number or email", await response.Content.ReadAsStringAsync());
        Assert.Empty(await OrdersForAsync(tenantId));
    }

    [Fact]
    public async Task Placing_an_order_empties_the_basket()
    {
        // Otherwise the customer's next visit starts with the things they have already ordered in
        // it, which is how the same order gets placed twice.
        var (subdomain, _) = await SeedShopAsync(Jollof());
        var client = CreateClient();
        var host = $"http://{subdomain}.platform.com";

        var cookie = await AddToCartAsync(client, host, "jollof-and-chicken", 1);
        var placed = await client.SendAsync(
            WithCookie(HttpMethod.Post, $"{host}/checkout", cookie, CheckoutForm()));

        var cleared = Assert.Single(placed.Headers.GetValues("Set-Cookie"));
        Assert.Contains($"{Cart.CookieName}=;", cleared);
    }

    [Fact]
    public async Task A_bot_filling_the_honeypot_places_no_order()
    {
        var (subdomain, tenantId) = await SeedShopAsync(Jollof());
        var client = CreateClient();
        var host = $"http://{subdomain}.platform.com";

        var cookie = await AddToCartAsync(client, host, "jollof-and-chicken", 1);

        var response = await client.SendAsync(WithCookie(
            HttpMethod.Post, $"{host}/checkout", cookie, CheckoutForm(honeypot: "http://spam.example")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(await OrdersForAsync(tenantId));
    }

    [Fact]
    public async Task An_empty_basket_cannot_be_checked_out()
    {
        var (subdomain, tenantId) = await SeedShopAsync(Jollof());
        var host = $"http://{subdomain}.platform.com";

        var response = await CreateClient().GetAsync($"{host}/checkout");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/cart", response.Headers.Location!.ToString());
        Assert.Empty(await OrdersForAsync(tenantId));
    }

    [Fact]
    public async Task One_tenants_order_is_never_readable_from_another_tenants_host()
    {
        var (mine, tenantId) = await SeedShopAsync(Jollof());
        var (theirs, _) = await SeedShopAsync(Jollof());
        var client = CreateClient();

        var cookie = await AddToCartAsync(client, $"http://{mine}.platform.com", "jollof-and-chicken", 1);
        await client.SendAsync(WithCookie(
            HttpMethod.Post, $"http://{mine}.platform.com/checkout", cookie, CheckoutForm()));

        var order = Assert.Single(await OrdersForAsync(tenantId));

        // Readable on the host that took it...
        Assert.Contains(order.Reference,
            await client.GetStringAsync($"http://{mine}.platform.com/order/{order.Reference}"));

        // ...and nowhere else, even with the reference in hand.
        var crossed = await client.GetAsync($"http://{theirs}.platform.com/order/{order.Reference}");
        Assert.Equal(HttpStatusCode.NotFound, crossed.StatusCode);
    }

    [Fact]
    public void An_order_reference_avoids_the_characters_people_misread()
    {
        // It gets read down a phone line and typed into a chat, so 0/O, 1/I/L and 5/S are out.
        var references = Enumerable.Range(0, 200).Select(_ => Order.NewReference()).ToList();

        Assert.All(references, reference =>
        {
            Assert.Equal(6, reference.Length);
            Assert.DoesNotContain(reference, r => "OIL01S5".Contains(r));
        });

        // And they are not a sequence: a guessable reference would let one customer ask after
        // another's order.
        Assert.True(references.Distinct().Count() > 190);
    }
}
