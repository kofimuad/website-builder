using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;

namespace WebsiteBuilder.Tests;

[Collection(nameof(PostgresCollection))]
public class TenantIsolationTests(PostgresFixture fixture)
{
    /// <summary>Seeds two tenants, each owning one site, and returns their ids.</summary>
    private async Task<(Guid TenantA, Guid TenantB)> SeedTwoTenantsAsync()
    {
        var tenantA = new Tenant { Subdomain = $"a-{Guid.NewGuid():N}"[..20], Name = "Tenant A" };
        var tenantB = new Tenant { Subdomain = $"b-{Guid.NewGuid():N}"[..20], Name = "Tenant B" };

        using (var db = fixture.CreateContext(new TenantContext()))
        {
            db.Tenants.AddRange(tenantA, tenantB);
            await db.SaveChangesAsync();
        }

        using (var db = fixture.CreateContext(tenantA.Id))
        {
            db.Sites.Add(new Site { Name = "Site A" });
            await db.SaveChangesAsync();
        }

        using (var db = fixture.CreateContext(tenantB.Id))
        {
            db.Sites.Add(new Site { Name = "Site B" });
            await db.SaveChangesAsync();
        }

        return (tenantA.Id, tenantB.Id);
    }

    [Fact]
    public async Task Query_returns_only_the_ambient_tenants_rows()
    {
        var (tenantA, _) = await SeedTwoTenantsAsync();

        using var db = fixture.CreateContext(tenantA);
        var sites = await db.Sites.ToListAsync();

        Assert.All(sites, s => Assert.Equal(tenantA, s.TenantId));
        Assert.Contains(sites, s => s.Name == "Site A");
        Assert.DoesNotContain(sites, s => s.Name == "Site B");
    }

    [Fact]
    public async Task Another_tenants_row_is_invisible_even_when_fetched_by_id()
    {
        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        Guid otherSiteId;
        using (var db = fixture.CreateContext(tenantB))
        {
            otherSiteId = (await db.Sites.SingleAsync(s => s.Name == "Site B")).Id;
        }

        using var asTenantA = fixture.CreateContext(tenantA);
        var leaked = await asTenantA.Sites.FirstOrDefaultAsync(s => s.Id == otherSiteId);

        Assert.Null(leaked);
    }

    [Fact]
    public async Task Query_without_a_tenant_in_scope_returns_nothing()
    {
        await SeedTwoTenantsAsync();

        using var db = fixture.CreateContext(new TenantContext());
        var sites = await db.Sites.ToListAsync();

        Assert.Empty(sites);
    }

    [Fact]
    public async Task Saving_without_a_tenant_in_scope_throws()
    {
        using var db = fixture.CreateContext(new TenantContext());
        db.Sites.Add(new Site { Name = "Orphan" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Creating_a_row_for_another_tenant_throws()
    {
        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        using var db = fixture.CreateContext(tenantA);
        db.Sites.Add(new Site { Name = "Smuggled", TenantId = tenantB });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task New_rows_are_stamped_with_the_ambient_tenant()
    {
        var (tenantA, _) = await SeedTwoTenantsAsync();

        using var db = fixture.CreateContext(tenantA);
        var site = new Site { Name = "Stamped" };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        Assert.Equal(tenantA, site.TenantId);
    }
}

public class DatabaseUrlTests
{
    [Fact]
    public void Converts_a_railway_style_url_to_an_npgsql_connection_string()
    {
        var result = DatabaseUrl.ToNpgsqlConnectionString(
            "postgresql://someuser:s3cr3t%40pw@monorail.proxy.rlwy.net:41234/railway");

        Assert.Contains("Host=monorail.proxy.rlwy.net", result);
        Assert.Contains("Port=41234", result);
        Assert.Contains("Database=railway", result);
        Assert.Contains("Username=someuser", result);
        Assert.Contains("Password=s3cr3t@pw", result);
    }

    [Fact]
    public void Leaves_a_key_value_connection_string_unchanged()
    {
        const string original = "Host=localhost;Database=websitebuilder;Username=postgres";

        Assert.Equal(original, DatabaseUrl.ToNpgsqlConnectionString(original));
    }
}
