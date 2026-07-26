using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Onboarding;

namespace WebsiteBuilder.Tests;

[Collection(nameof(PostgresCollection))]
public class OnboardingProgressAndPreviewTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private sealed class CollectingProgress : IProgress<OnboardingProgress>
    {
        public List<OnboardingProgress> Stages { get; } = [];

        // OnboardingService reports synchronously inline, so appends stay in order.
        public void Report(OnboardingProgress value) => Stages.Add(value);
    }

    private static BusinessProfile Answers(string? name = null) => new()
    {
        BusinessName = name ?? $"Progress Co {Guid.NewGuid():N}",
        Category = "plumber",
        Offerings = ["Drain clearing"],
        PhoneNumber = "+233200000000",
        ServiceArea = "Osu",
    };

    private async Task<OnboardingResult> OnboardAsync(Guid ownerId, string? name = null, IProgress<OnboardingProgress>? progress = null)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OnboardingService>()
            .CompleteAsync(Answers(name), ownerId, progress);
    }

    [Fact]
    public async Task Completing_onboarding_reports_the_real_stages_in_order()
    {
        var progress = new CollectingProgress();

        await OnboardAsync(await _factory.CreateOwnerAsync(), progress: progress);

        Assert.Equal(
            [
                OnboardingProgress.Preparing,
                OnboardingProgress.WritingCopy,
                OnboardingProgress.BuildingPages,
                OnboardingProgress.Finishing,
            ],
            progress.Stages);
    }

    [Fact]
    public async Task Onboarding_gives_the_new_tenant_to_the_signed_in_owner()
    {
        var ownerId = await _factory.CreateOwnerAsync();
        var result = await OnboardAsync(ownerId, "Owned Co");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Id == result.TenantId);

        Assert.Equal(ownerId, tenant.OwnerId);
    }

    [Fact]
    public async Task Onboarding_without_an_owner_is_refused()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<OnboardingService>();

        // A tenant with no owner would be unreachable the moment it existed.
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CompleteAsync(Answers("Ownerless Co"), Guid.Empty));
    }

    [Fact]
    public async Task The_draft_can_be_previewed_before_it_is_published()
    {
        var ownerId = await _factory.CreateOwnerAsync();
        var result = await OnboardAsync(ownerId, "Preview Co");

        var response = await _factory.CreateClientAs(ownerId).GetAsync($"http://platform.com/preview/{result.SiteId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Preview Co", html);
    }

    [Fact]
    public async Task Previewing_an_unknown_site_is_not_found()
    {
        var ownerId = await _factory.CreateOwnerAsync();

        var response = await _factory.CreateClientAs(ownerId).GetAsync($"http://platform.com/preview/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Another_owners_draft_cannot_be_previewed()
    {
        var owner = await _factory.CreateOwnerAsync();
        var result = await OnboardAsync(owner, "Private Co");

        var stranger = await _factory.CreateOwnerAsync();
        var response = await _factory.CreateClientAs(stranger).GetAsync($"http://platform.com/preview/{result.SiteId}");

        // Not-found rather than forbidden: a stranger must not learn the site id is real.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_visitor_cannot_preview_a_draft()
    {
        var ownerId = await _factory.CreateOwnerAsync();
        var result = await OnboardAsync(ownerId, "Signed In Only Co");

        var response = await _factory.CreateAnonymousClient().GetAsync($"http://platform.com/preview/{result.SiteId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/signin", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_draft_preview_does_not_require_the_site_to_be_published()
    {
        var ownerId = await _factory.CreateOwnerAsync();
        var result = await OnboardAsync(ownerId, "Unpublished Co");

        // The tenant host still shows nothing (not published)...
        var live = await _factory.CreateClient().GetAsync($"http://{result.Subdomain}.platform.com/");
        Assert.Equal(HttpStatusCode.NotFound, live.StatusCode);

        // ...but the owner's draft preview works.
        var preview = await _factory.CreateClientAs(ownerId).GetAsync($"http://platform.com/preview/{result.SiteId}");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
    }
}
