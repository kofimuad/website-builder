using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Web.Shop;

namespace WebsiteBuilder.Tests;

public class CartTests
{
    private static readonly Guid Jollof = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Banku = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void A_cart_round_trips_through_its_cookie()
    {
        var cart = new Cart();
        cart.Add(Jollof, 2);
        cart.Add(Banku, 1);

        var restored = Cart.Parse(cart.ToString());

        Assert.Equal(2, restored.QuantityOf(Jollof));
        Assert.Equal(1, restored.QuantityOf(Banku));
        Assert.Equal(3, restored.TotalItems);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nonsense")]
    [InlineData("not-a-guid:2")]
    [InlineData("11111111-1111-1111-1111-111111111111:not-a-number")]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    public void A_malformed_cookie_produces_an_empty_cart_rather_than_an_error(string? cookie)
    {
        // The value came from a browser. A corrupt cart must not put an error page between a
        // customer and their order.
        Assert.True(Cart.Parse(cookie).IsEmpty);
    }

    [Fact]
    public void A_good_line_survives_a_broken_one_beside_it()
    {
        var cart = Cart.Parse($"garbage,{Jollof:N}:3");

        Assert.Equal(3, cart.QuantityOf(Jollof));
    }

    [Fact]
    public void Adding_the_same_product_twice_increases_its_quantity()
    {
        var cart = new Cart();
        cart.Add(Jollof, 1);
        cart.Add(Jollof, 2);

        Assert.Equal(3, cart.QuantityOf(Jollof));
    }

    [Fact]
    public void Setting_a_quantity_to_zero_removes_the_line()
    {
        var cart = new Cart();
        cart.Add(Jollof, 2);
        cart.Set(Jollof, 0);

        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public void Quantities_are_clamped_rather_than_rejected()
    {
        // A hand-written cookie asking for 100000 of something must not become an order for it.
        var cart = Cart.Parse($"{Jollof:N}:100000");

        Assert.Equal(Cart.MaxQuantity, cart.QuantityOf(Jollof));
    }

    [Fact]
    public void A_cookie_cannot_grow_the_cart_without_limit()
    {
        var lines = string.Join(',', Enumerable.Range(0, 500).Select(_ => $"{Guid.NewGuid():N}:1"));

        Assert.True(Cart.Parse(lines).ProductIds.Count <= Cart.MaxLines);
    }

    [Fact]
    public void Products_that_have_gone_away_are_dropped()
    {
        var cart = new Cart();
        cart.Add(Jollof, 1);
        cart.Add(Banku, 1);

        cart.KeepOnly([Jollof]);

        Assert.Equal(1, cart.QuantityOf(Jollof));
        Assert.Equal(0, cart.QuantityOf(Banku));
    }
}

public class OrderMessageTests
{
    private static Product Product(string name, long? priceMinor, string currency = "GHS") => new()
    {
        Name = name,
        Slug = name.ToLowerInvariant(),
        PriceMinor = priceMinor,
        Currency = currency,
    };

    [Fact]
    public void The_message_lists_every_line_and_the_total()
    {
        var lines = new List<PricedLine>
        {
            new(Product("Jollof and chicken", 3000), 2),
            new(Product("Banku and tilapia", 4500), 1),
        };

        var message = OrderMessage.Compose("Auntie Ako's Kitchen", lines);

        Assert.Contains("Hello Auntie Ako's Kitchen", message);
        Assert.Contains("2 x Jollof and chicken (GHS 60.00)", message);
        Assert.Contains("1 x Banku and tilapia (GHS 45.00)", message);
        Assert.Contains("Total: GHS 105.00", message);
    }

    [Fact]
    public void The_message_reads_the_same_whichever_operating_system_built_it()
    {
        // Production is Linux, development is Windows. AppendLine would make the order differ.
        var message = OrderMessage.Compose("Kitchen", [new PricedLine(Product("Jollof", 3000), 1)]);

        Assert.DoesNotContain('\r', message);
        Assert.Contains('\n', message);
    }

    [Fact]
    public void An_unpriced_item_is_listed_without_inventing_a_figure()
    {
        var lines = new List<PricedLine> { new(Product("Catering", null), 1) };

        var message = OrderMessage.Compose("Auntie Ako's Kitchen", lines);

        Assert.Contains("1 x Catering", message);
        Assert.DoesNotContain("Total:", message);
    }

    [Fact]
    public void Mixed_currencies_produce_no_total_rather_than_a_wrong_one()
    {
        var lines = new List<PricedLine>
        {
            new(Product("Local dish", 3000), 1),
            new(Product("Import", 1000, "USD"), 1),
        };

        var (total, currency) = OrderMessage.Total(lines);

        Assert.Null(total);
        Assert.Null(currency);
        Assert.DoesNotContain("Total:", OrderMessage.Compose("Kitchen", lines));
    }

    [Fact]
    public void Unpriced_lines_do_not_stop_the_priced_ones_being_totalled()
    {
        var lines = new List<PricedLine>
        {
            new(Product("Jollof", 3000), 2),
            new(Product("Catering", null), 1),
        };

        var (total, currency) = OrderMessage.Total(lines);

        Assert.Equal(6000, total);
        Assert.Equal("GHS", currency);
    }

    [Fact]
    public void The_link_opens_whatsapp_with_the_order_already_written()
    {
        var link = OrderMessage.Link("+233200000000", "Hello there, I'd like to order:\n- 1 x Jollof");

        Assert.StartsWith("https://wa.me/233200000000?text=", link);
        // Encoded, or WhatsApp receives a truncated message at the first space.
        Assert.DoesNotContain(" ", link);
        Assert.Contains("Jollof", Uri.UnescapeDataString(link));
    }
}
