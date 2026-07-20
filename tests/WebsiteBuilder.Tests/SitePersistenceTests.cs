using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Tests;

[Collection(nameof(PostgresCollection))]
public class SitePersistenceTests(PostgresFixture fixture)
{
    private async Task<Guid> SeedTenantAsync()
    {
        var tenant = new Tenant { Subdomain = $"s{Guid.NewGuid():N}"[..12], Name = "Persistence Tenant" };

        using var db = fixture.CreateContext(new TenantContext());
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        return tenant.Id;
    }

    private async Task<Guid> SeedSiteAsync(Guid tenantId)
    {
        using var db = fixture.CreateContext(tenantId);
        var site = new Site
        {
            Name = "Joe's Plumbing",
            Draft = new SiteDefinition
            {
                Meta = new SiteMeta { BusinessName = "Joe's Plumbing" },
                Sections = [new HeroSection { Headline = "Original headline" }],
            },
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        return site.Id;
    }

    [Fact]
    public async Task A_site_definition_round_trips_through_jsonb()
    {
        var tenantId = await SeedTenantAsync();
        var siteId = await SeedSiteAsync(tenantId);

        using var db = fixture.CreateContext(tenantId);
        var site = await db.Sites.SingleAsync(s => s.Id == siteId);

        Assert.Equal("Joe's Plumbing", site.Draft.Meta.BusinessName);
        Assert.Equal("Original headline", Assert.IsType<HeroSection>(site.Draft.Sections[0]).Headline);
        Assert.Null(site.Published);
    }

    [Fact]
    public async Task Definitions_are_stored_as_real_jsonb_and_are_queryable()
    {
        var tenantId = await SeedTenantAsync();
        var siteId = await SeedSiteAsync(tenantId);

        using var db = fixture.CreateContext(tenantId);
        var businessName = await db.Database
            .SqlQuery<string>($"""SELECT "Draft" -> 'meta' ->> 'businessName' AS "Value" FROM "Sites" WHERE "Id" = {siteId}""")
            .SingleAsync();

        Assert.Equal("Joe's Plumbing", businessName);
    }

    [Fact]
    public async Task An_in_place_edit_to_the_draft_is_detected_and_saved()
    {
        var tenantId = await SeedTenantAsync();
        var siteId = await SeedSiteAsync(tenantId);

        using (var db = fixture.CreateContext(tenantId))
        {
            var site = await db.Sites.SingleAsync(s => s.Id == siteId);
            // No reassignment of site.Draft: without a value comparer EF sees no change here.
            ((HeroSection)site.Draft.Sections[0]).Headline = "Edited in place";
            await db.SaveChangesAsync();
        }

        using (var db = fixture.CreateContext(tenantId))
        {
            var site = await db.Sites.SingleAsync(s => s.Id == siteId);
            Assert.Equal("Edited in place", Assert.IsType<HeroSection>(site.Draft.Sections[0]).Headline);
        }
    }

    [Fact]
    public async Task Publishing_and_further_draft_edits_stay_independent_across_a_save()
    {
        var tenantId = await SeedTenantAsync();
        var siteId = await SeedSiteAsync(tenantId);

        using (var db = fixture.CreateContext(tenantId))
        {
            var site = await db.Sites.SingleAsync(s => s.Id == siteId);
            site.Publish();
            await db.SaveChangesAsync();
        }

        using (var db = fixture.CreateContext(tenantId))
        {
            var site = await db.Sites.SingleAsync(s => s.Id == siteId);
            ((HeroSection)site.Draft.Sections[0]).Headline = "Draft moved on";
            await db.SaveChangesAsync();
        }

        using (var db = fixture.CreateContext(tenantId))
        {
            var site = await db.Sites.SingleAsync(s => s.Id == siteId);
            Assert.Equal("Draft moved on", Assert.IsType<HeroSection>(site.Draft.Sections[0]).Headline);
            Assert.Equal("Original headline", Assert.IsType<HeroSection>(site.Published!.Sections[0]).Headline);
            Assert.NotNull(site.PublishedUtc);
        }
    }

    [Fact]
    public async Task Sites_remain_tenant_isolated_now_that_they_carry_definitions()
    {
        var tenantA = await SeedTenantAsync();
        var tenantB = await SeedTenantAsync();
        await SeedSiteAsync(tenantA);

        using var db = fixture.CreateContext(tenantB);
        Assert.Empty(await db.Sites.ToListAsync());
    }
}
