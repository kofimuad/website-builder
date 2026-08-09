using Microsoft.Extensions.DependencyInjection;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Shop;

namespace WebsiteBuilder.Tests;

/// <summary>The owner's side of the catalog, against a real database and the tenant filter.</summary>
[Collection(nameof(PostgresCollection))]
public class ProductsServiceTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    /// <summary>Creates a tenant and returns a scope already pointed at it, as the editor's would be.</summary>
    private async Task<(IServiceScope Scope, ProductsService Products, Guid TenantId)> ScopeForNewTenantAsync()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();

        var tenant = new Tenant { Subdomain = $"p{Guid.NewGuid():N}"[..12], Name = "Shop" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        tenantContext.TenantId = tenant.Id;

        return (scope, scope.ServiceProvider.GetRequiredService<ProductsService>(), tenant.Id);
    }

    [Fact]
    public async Task A_new_product_gets_a_slug_from_its_name()
    {
        var (scope, products, _) = await ScopeForNewTenantAsync();
        using var _s = scope;

        var product = await products.AddAsync("Jollof & Chicken");

        Assert.Equal("jollof-chicken", product.Slug);
    }

    [Fact]
    public async Task Two_products_with_the_same_name_get_different_addresses()
    {
        var (scope, products, _) = await ScopeForNewTenantAsync();
        using var _s = scope;

        var first = await products.AddAsync("Jollof");
        var second = await products.AddAsync("Jollof");

        Assert.Equal("jollof", first.Slug);
        Assert.Equal("jollof-2", second.Slug);
    }

    [Fact]
    public async Task A_name_with_nothing_usable_in_it_still_gets_an_address()
    {
        var (scope, products, _) = await ScopeForNewTenantAsync();
        using var _s = scope;

        var product = await products.AddAsync("!!!");

        Assert.NotEmpty(product.Slug);
    }

    [Fact]
    public async Task Two_businesses_may_both_sell_jollof()
    {
        // The slug is unique per tenant, not globally: the index says so and this proves it.
        var (firstScope, firstProducts, _) = await ScopeForNewTenantAsync();
        using var _f = firstScope;
        var mine = await firstProducts.AddAsync("Jollof");

        var (secondScope, secondProducts, _) = await ScopeForNewTenantAsync();
        using var _s = secondScope;
        var theirs = await secondProducts.AddAsync("Jollof");

        Assert.Equal("jollof", mine.Slug);
        Assert.Equal("jollof", theirs.Slug);
    }

    [Fact]
    public async Task One_tenants_list_never_includes_anothers()
    {
        var (firstScope, firstProducts, _) = await ScopeForNewTenantAsync();
        using var _f = firstScope;
        await firstProducts.AddAsync("Mine");

        var (secondScope, secondProducts, _) = await ScopeForNewTenantAsync();
        using var _s = secondScope;
        await secondProducts.AddAsync("Theirs");

        var theirs = await secondProducts.ListAsync();

        Assert.Equal(["Theirs"], theirs.Select(p => p.Name));
    }

    [Fact]
    public async Task An_edit_that_is_not_a_rename_leaves_the_address_alone()
    {
        var (scope, products, _) = await ScopeForNewTenantAsync();
        using var _s = scope;

        var product = await products.AddAsync("Jollof");
        product.Description = "Now with a description";
        await products.SaveAsync(product);

        Assert.Equal("jollof", product.Slug);
    }

    [Fact]
    public async Task Renaming_a_product_moves_its_address()
    {
        // Everything is added as "New item", so an address that did not follow the name left the
        // whole catalog sitting at /products/new-item-2.
        var (scope, products, _) = await ScopeForNewTenantAsync();
        using var _s = scope;

        var product = await products.AddAsync("New item");
        Assert.Equal("new-item", product.Slug);

        product.Name = "Balloon arch";
        await products.SaveAsync(product);

        Assert.Equal("balloon-arch", product.Slug);
    }

    [Fact]
    public async Task A_rename_cannot_collide_with_another_products_address()
    {
        var (scope, products, _) = await ScopeForNewTenantAsync();
        using var _s = scope;

        await products.AddAsync("Balloon arch");
        var second = await products.AddAsync("Something else");

        second.Name = "Balloon arch";
        await products.SaveAsync(second);

        Assert.Equal("balloon-arch-2", second.Slug);
    }

    [Fact]
    public async Task A_name_cleared_to_nothing_still_leaves_a_usable_product()
    {
        // The page saves on blur, so an empty name does reach here. It must not produce a product
        // with no name and no address.
        var (scope, products, _) = await ScopeForNewTenantAsync();
        using var _s = scope;

        var product = await products.AddAsync("Jollof");
        product.Name = "   ";
        await products.SaveAsync(product);

        Assert.False(string.IsNullOrWhiteSpace(product.Name));
        Assert.False(string.IsNullOrWhiteSpace(product.Slug));
    }

    [Fact]
    public async Task A_blank_currency_falls_back_rather_than_being_stored_empty()
    {
        var (scope, products, _) = await ScopeForNewTenantAsync();
        using var _s = scope;

        var product = await products.AddAsync("Jollof");
        product.Currency = "  ";
        await products.SaveAsync(product);

        Assert.Equal("GHS", product.Currency);
    }

    [Fact]
    public async Task New_products_are_added_to_the_end_of_the_owners_order()
    {
        var (scope, products, _) = await ScopeForNewTenantAsync();
        using var _s = scope;

        await products.AddAsync("First");
        await products.AddAsync("Second");
        await products.AddAsync("Third");

        var listed = await products.ListAsync();

        Assert.Equal(["First", "Second", "Third"], listed.Select(p => p.Name));
    }
}
