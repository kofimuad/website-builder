using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Management;
using WebsiteBuilder.Web.Onboarding;

namespace WebsiteBuilder.Tests;

/// <summary>
/// The gate that stops one business reading or editing another's site (WB-15). Before sign-in, a
/// site id was the only thing protecting a tenant's draft copy and its customers' contact details.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class SiteOwnershipTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private async Task<OnboardingResult> OnboardAsync(Guid ownerId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OnboardingService>().CompleteAsync(
            new BusinessProfile
            {
                BusinessName = name,
                Category = "plumber",
                Offerings = ["Drain clearing"],
                PhoneNumber = "+233200000000",
            },
            ownerId);
    }

    private async Task<ManagedSite?> LoadAsAsync(Guid siteId, Guid? ownerId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SiteManagementService>().LoadAsync(siteId, ownerId);
    }

    [Fact]
    public async Task An_owner_can_load_their_own_site()
    {
        var ownerId = await _factory.CreateOwnerAsync();
        var result = await OnboardAsync(ownerId, $"Mine {Guid.NewGuid():N}");

        var managed = await LoadAsAsync(result.SiteId, ownerId);

        Assert.NotNull(managed);
        Assert.Equal(result.SiteId, managed!.Site.Id);
    }

    [Fact]
    public async Task Another_owner_cannot_load_the_site()
    {
        var owner = await _factory.CreateOwnerAsync();
        var result = await OnboardAsync(owner, $"Theirs {Guid.NewGuid():N}");

        var stranger = await _factory.CreateOwnerAsync();

        Assert.Null(await LoadAsAsync(result.SiteId, stranger));
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_load_any_site()
    {
        var owner = await _factory.CreateOwnerAsync();
        var result = await OnboardAsync(owner, $"Anon {Guid.NewGuid():N}");

        Assert.Null(await LoadAsAsync(result.SiteId, ownerId: null));
    }

    [Fact]
    public async Task A_refused_load_leaves_no_tenant_in_scope()
    {
        var owner = await _factory.CreateOwnerAsync();
        var result = await OnboardAsync(owner, $"Scope {Guid.NewGuid():N}");
        var stranger = await _factory.CreateOwnerAsync();

        using var scope = _factory.Services.CreateScope();
        var managed = await scope.ServiceProvider.GetRequiredService<SiteManagementService>()
            .LoadAsync(result.SiteId, stranger);

        Assert.Null(managed);

        // The important half: a rejected load must not have granted tenant scope on its way out,
        // or every tenant-filtered query afterwards would quietly read the wrong business's data.
        Assert.Null(scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId);
    }

    [Fact]
    public async Task A_tenant_with_no_owner_cannot_be_managed_by_anyone()
    {
        // Tenants created before sign-in existed, and the demo seeder's, have no owner.
        Guid siteId;
        var subdomain = $"t{Guid.NewGuid():N}"[..12];

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            var tenant = new Tenant { Subdomain = subdomain, Name = "Ownerless", OwnerId = null };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenant.Id;
            var site = new Site
            {
                Name = "Ownerless site",
                Draft = new SiteDefinition { Sections = [new HeroSection { Headline = "Hi" }] },
            };
            db.Sites.Add(site);
            await db.SaveChangesAsync();
            siteId = site.Id;
        }

        var someone = await _factory.CreateOwnerAsync();

        Assert.Null(await LoadAsAsync(siteId, someone));
    }

    [Fact]
    public async Task Leads_belong_to_the_owner_who_owns_the_site()
    {
        var owner = await _factory.CreateOwnerAsync();
        var result = await OnboardAsync(owner, $"Leads {Guid.NewGuid():N}");

        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = result.TenantId;
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            db.Leads.Add(new Lead
            {
                SiteId = result.SiteId,
                Name = "Ama Boateng",
                PhoneNumber = "+233209999999",
                Message = "Blocked drain, need someone today",
            });
            await db.SaveChangesAsync();
        }

        var stranger = await _factory.CreateOwnerAsync();

        // The inbox reaches leads only through LoadAsync, so refusing there is what protects a
        // customer's phone number from a competitor holding the site id.
        Assert.Null(await LoadAsAsync(result.SiteId, stranger));

        var mine = await LoadAsAsync(result.SiteId, owner);
        Assert.NotNull(mine);
    }

    [Fact]
    public async Task The_dashboard_lists_only_the_owners_own_sites()
    {
        var owner = await _factory.CreateOwnerAsync();
        var stranger = await _factory.CreateOwnerAsync();

        var first = await OnboardAsync(owner, $"First {Guid.NewGuid():N}");
        var second = await OnboardAsync(owner, $"Second {Guid.NewGuid():N}");
        var theirs = await OnboardAsync(stranger, $"Theirs {Guid.NewGuid():N}");

        using var scope = _factory.Services.CreateScope();
        var mine = await scope.ServiceProvider.GetRequiredService<SiteManagementService>()
            .ListForOwnerAsync(owner);

        var ids = mine.Select(s => s.SiteId).ToList();

        Assert.Contains(first.SiteId, ids);
        Assert.Contains(second.SiteId, ids);
        Assert.DoesNotContain(theirs.SiteId, ids);
    }

    [Fact]
    public async Task The_dashboard_counts_unread_leads_per_site()
    {
        var owner = await _factory.CreateOwnerAsync();
        var result = await OnboardAsync(owner, $"Counting {Guid.NewGuid():N}");

        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = result.TenantId;
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

            db.Leads.Add(new Lead { SiteId = result.SiteId, Name = "A", Message = "One", IsRead = true });
            db.Leads.Add(new Lead { SiteId = result.SiteId, Name = "B", Message = "Two" });
            db.Leads.Add(new Lead { SiteId = result.SiteId, Name = "C", Message = "Three" });
            await db.SaveChangesAsync();
        }

        using var readScope = _factory.Services.CreateScope();
        var site = (await readScope.ServiceProvider.GetRequiredService<SiteManagementService>()
            .ListForOwnerAsync(owner))
            .Single(s => s.SiteId == result.SiteId);

        Assert.Equal(3, site.TotalLeads);
        Assert.Equal(2, site.UnreadLeads);
    }

    [Fact]
    public async Task An_anonymous_visitor_is_sent_to_sign_in_from_the_dashboard()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("http://platform.com/dashboard");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/signin", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task An_anonymous_visitor_is_sent_to_sign_in_from_the_editor()
    {
        var owner = await _factory.CreateOwnerAsync();
        var result = await OnboardAsync(owner, $"Gated {Guid.NewGuid():N}");

        var response = await _factory.CreateAnonymousClient()
            .GetAsync($"http://platform.com/manage/{result.SiteId}/edit");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/signin", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_sign_in_page_itself_is_reachable_without_signing_in()
    {
        var response = await _factory.CreateAnonymousClient(allowAutoRedirect: true)
            .GetAsync("http://platform.com/signin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sign in to Sitely", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_published_site_is_still_public()
    {
        var owner = await _factory.CreateOwnerAsync();
        var result = await OnboardAsync(owner, $"Public {Guid.NewGuid():N}");

        using (var scope = _factory.Services.CreateScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<SiteManagementService>();
            await mgmt.LoadAsync(result.SiteId, owner);
            await mgmt.PublishAsync(result.SiteId);
        }

        // Sign-in guards the builder, never the customer-facing site.
        var response = await _factory.CreateClient().GetAsync($"http://{result.Subdomain}.platform.com/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
