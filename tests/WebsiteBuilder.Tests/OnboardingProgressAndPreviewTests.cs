using System.Net;
using Microsoft.Extensions.DependencyInjection;
using WebsiteBuilder.Core.Onboarding;
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

    [Fact]
    public async Task Completing_onboarding_reports_the_real_stages_in_order()
    {
        var progress = new CollectingProgress();

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OnboardingService>().CompleteAsync(Answers(), progress);

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
    public async Task The_draft_can_be_previewed_before_it_is_published()
    {
        OnboardingResult result;
        using (var scope = _factory.Services.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<OnboardingService>().CompleteAsync(Answers("Preview Co"));
        }

        var response = await _factory.CreateClient().GetAsync($"http://platform.com/preview/{result.SiteId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Preview Co", html);
    }

    [Fact]
    public async Task Previewing_an_unknown_site_is_not_found()
    {
        var response = await _factory.CreateClient().GetAsync($"http://platform.com/preview/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_draft_preview_does_not_require_the_site_to_be_published()
    {
        OnboardingResult result;
        using (var scope = _factory.Services.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<OnboardingService>().CompleteAsync(Answers("Unpublished Co"));
        }

        // The tenant host still shows nothing (not published)...
        var live = await _factory.CreateClient().GetAsync($"http://{result.Subdomain}.platform.com/");
        Assert.Equal(HttpStatusCode.NotFound, live.StatusCode);

        // ...but the draft preview works.
        var preview = await _factory.CreateClient().GetAsync($"http://platform.com/preview/{result.SiteId}");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
    }
}
