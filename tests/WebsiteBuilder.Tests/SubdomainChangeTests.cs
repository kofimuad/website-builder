using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Management;
using WebsiteBuilder.Web.Onboarding;

namespace WebsiteBuilder.Tests;

/// <summary>
/// Choosing the address at first publish (WB-28). The address is the one thing about a site that
/// other people write down, so these cover who may change it and when.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class SubdomainChangeTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private Guid _ownerId;

    private async Task<Guid> OwnerAsync() =>
        _ownerId == Guid.Empty ? _ownerId = await _factory.CreateOwnerAsync() : _ownerId;

    private async Task<OnboardingResult> OnboardAsync(string name)
    {
        var ownerId = await OwnerAsync();

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

    [Fact]
    public async Task An_unpublished_site_can_move_to_a_free_address()
    {
        var result = await OnboardAsync($"Movable {Guid.NewGuid():N}");
        var wanted = $"chosen-{Guid.NewGuid():N}"[..20];

        using (var scope = _factory.Services.CreateScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<SiteManagementService>();
            await mgmt.LoadAsync(result.SiteId, _ownerId);

            Assert.Equal(SubdomainProblem.None, await mgmt.ChangeSubdomainAsync(result.SiteId, wanted));
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Id == result.TenantId);

            Assert.Equal(wanted, tenant.Subdomain);
        }
    }

    [Fact]
    public async Task An_address_someone_already_holds_is_refused()
    {
        var first = await OnboardAsync($"Holder {Guid.NewGuid():N}");
        var second = await OnboardAsync($"Wanter {Guid.NewGuid():N}");

        string taken;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            taken = (await db.Tenants.SingleAsync(t => t.Id == first.TenantId)).Subdomain;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<SiteManagementService>();
            await mgmt.LoadAsync(second.SiteId, _ownerId);

            Assert.Equal(SubdomainProblem.Taken, await mgmt.ChangeSubdomainAsync(second.SiteId, taken));
        }

        // And the loser keeps the address it had, rather than being left with nothing.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Id == second.TenantId);

            Assert.NotEqual(taken, tenant.Subdomain);
        }
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("send")]
    [InlineData("login")]
    public async Task A_reserved_address_is_refused(string reserved)
    {
        var result = await OnboardAsync($"Reserved {Guid.NewGuid():N}");

        using var scope = _factory.Services.CreateScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<SiteManagementService>();
        await mgmt.LoadAsync(result.SiteId, _ownerId);

        Assert.Equal(SubdomainProblem.Reserved, await mgmt.ChangeSubdomainAsync(result.SiteId, reserved));
    }

    [Fact]
    public async Task A_published_site_refuses_to_move_because_shared_links_would_break()
    {
        var result = await OnboardAsync($"Published {Guid.NewGuid():N}");

        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = result.TenantId;
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            (await db.Sites.SingleAsync()).Publish();
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<SiteManagementService>();
            await mgmt.LoadAsync(result.SiteId, _ownerId);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => mgmt.ChangeSubdomainAsync(result.SiteId, $"renamed-{Guid.NewGuid():N}"[..18]));
        }
    }

    [Fact]
    public async Task An_address_reads_as_taken_even_when_it_is_the_sites_own()
    {
        // The check answers "is this free?" honestly, and an owner's own address is not free.
        // Knowing that the unchanged address is fine belongs to the caller, which is why the
        // editor short-circuits before asking — otherwise opening the dialog and typing nothing
        // would show the owner an error about their own site.
        var result = await OnboardAsync($"Current {Guid.NewGuid():N}");

        using var scope = _factory.Services.CreateScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<SiteManagementService>();
        var managed = await mgmt.LoadAsync(result.SiteId, _ownerId);

        Assert.Equal(SubdomainProblem.Taken, await mgmt.CheckSubdomainAsync(managed!.Subdomain));
    }

    [Fact]
    public async Task Loading_a_site_reports_the_address_it_lives_at()
    {
        var result = await OnboardAsync($"Addressed {Guid.NewGuid():N}");

        using var scope = _factory.Services.CreateScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<SiteManagementService>();
        var managed = await mgmt.LoadAsync(result.SiteId, _ownerId);

        Assert.Equal(result.Subdomain, managed!.Subdomain);
    }
}
