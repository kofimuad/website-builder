using System.Net;
using Microsoft.Extensions.DependencyInjection;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Web.Onboarding;

namespace WebsiteBuilder.Tests;

/// <summary>
/// The onboarding live preview renders through the real site renderer rather than a mock, which
/// means it is a public, anonymous page serving a document built from unsaved answers. These pin
/// the parts of that which are easy to break.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class OnboardingLivePreviewTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void A_preview_is_read_repeatedly_rather_than_redeemed_once()
    {
        // The difference from OnboardingDraftStore, and the reason this is a separate store: a
        // stashed interview is taken exactly once, but a preview is re-read on every edit. If this
        // ever starts consuming, the preview goes blank the moment the visitor types twice.
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<OnboardingPreviewStore>();

        var token = OnboardingPreviewStore.NewToken();
        store.Put(token, Answers());

        Assert.NotNull(store.Get(token));
        Assert.NotNull(store.Get(token));
        Assert.NotNull(store.Get(token));
    }

    [Fact]
    public void Forgetting_a_preview_leaves_nothing_behind()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<OnboardingPreviewStore>();

        var token = OnboardingPreviewStore.NewToken();
        store.Put(token, Answers());
        store.Forget(token);

        Assert.Null(store.Get(token));
    }

    [Fact]
    public void Preview_tokens_are_unguessable_and_never_repeat()
    {
        // The token is the only thing guarding one visitor's half-finished answers from another,
        // because the page is necessarily anonymous — onboarding happens before sign-in.
        var tokens = Enumerable.Range(0, 200).Select(_ => OnboardingPreviewStore.NewToken()).ToList();

        Assert.Equal(tokens.Count, tokens.Distinct().Count());
        Assert.All(tokens, token => Assert.Equal(32, token.Length));
    }

    [Fact]
    public async Task An_expired_token_explains_itself_instead_of_failing()
    {
        // The store is in memory, so a restart or a slow interview loses the entry. That has to
        // read as a sentence in the frame, not a 500 or a stack trace beside the questions.
        var response = await _factory.CreateClient()
            .GetAsync($"http://platform.com/start/preview/{OnboardingPreviewStore.NewToken()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("expired", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_preview_is_rendered_by_the_real_site_renderer()
    {
        // The point of the whole exercise. If this page ever stops going through _RenderedSite,
        // the preview silently becomes a second implementation again and starts drifting — which
        // is exactly what it replaced.
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<OnboardingPreviewStore>();

        var token = OnboardingPreviewStore.NewToken();
        store.Put(token, Answers());

        var html = await _factory.CreateClient().GetStringAsync($"http://platform.com/start/preview/{token}");

        // Markers that only the real renderer and its stylesheet produce.
        Assert.Contains("Mensah Plumbing", html);
        Assert.Contains("hero-highlights", html);
        Assert.Contains("class=\"topbar\"", html);
        Assert.Contains("site-footer", html);
    }

    [Fact]
    public async Task An_unmatched_business_previews_with_no_photograph()
    {
        // Kofi's report: typing what you do swapped in a stock picture that did not match the
        // business. The fallback template carries no photography now, and the preview is the place
        // that was showing it.
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<OnboardingPreviewStore>();

        var answers = Answers();
        answers.Category = "mango stand";

        var token = OnboardingPreviewStore.NewToken();
        store.Put(token, answers);

        var html = await _factory.CreateClient().GetStringAsync($"http://platform.com/start/preview/{token}");

        Assert.DoesNotContain("images.unsplash.com", html);
        // The markup, not the word: the stylesheet is inlined, so ".hero-photo" appears in every
        // document whether or not the element does.
        Assert.DoesNotContain("class=\"hero-photo\"", html);
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public async Task A_matched_business_still_previews_with_its_curated_photograph()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<OnboardingPreviewStore>();

        var answers = Answers();
        answers.Category = "restaurant";

        var token = OnboardingPreviewStore.NewToken();
        store.Put(token, answers);

        var html = await _factory.CreateClient().GetStringAsync($"http://platform.com/start/preview/{token}");

        Assert.Contains("images.unsplash.com", html);
        Assert.Contains("class=\"hero hero-has-photo\"", html);
    }

    private static BusinessProfile Answers() => new()
    {
        BusinessName = "Mensah Plumbing",
        Category = "plumber",
        Offerings = ["Drain clearing", "Leak repair"],
        PhoneNumber = "+233200000000",
    };
}
