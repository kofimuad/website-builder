using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebsiteBuilder.Core.Generation;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Tests;

public class SectionTextTests
{
    private static System.Text.Json.Nodes.JsonObject ToNode(SiteSection section) =>
        (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(
            JsonSerializer.Serialize(section, SiteDefinitionSerializer.Options))!;

    [Fact]
    public void Collect_returns_prose_fields_and_skips_ids_types_and_urls()
    {
        var node = ToNode(new HeroSection
        {
            Headline = "Hi",
            Subheadline = "There",
            CallToActionLabel = "Call",
            CallToActionUrl = "tel:+1",
        });

        var paths = SectionText.Collect(node).Select(f => f.Path).ToList();

        Assert.Contains("headline", paths);
        Assert.Contains("subheadline", paths);
        Assert.Contains("callToActionLabel", paths);
        Assert.DoesNotContain("callToActionUrl", paths); // URL excluded
        Assert.DoesNotContain("type", paths);
        Assert.DoesNotContain("id", paths);
    }

    [Fact]
    public void Collect_reaches_into_list_items()
    {
        var node = ToNode(new ServicesSection
        {
            Heading = "Services",
            Items = [new ServiceItem { Title = "Drains", Description = "Cleared" }],
        });

        var paths = SectionText.Collect(node).Select(f => f.Path).ToList();

        Assert.Contains("items[0].title", paths);
        Assert.Contains("items[0].description", paths);
    }

    [Fact]
    public void Apply_changes_only_the_given_paths()
    {
        var node = ToNode(new ServicesSection
        {
            Heading = "Services",
            Items = [new ServiceItem { Title = "Drains", Description = "old" }],
        });

        SectionText.Apply(node, new Dictionary<string, string> { ["items[0].description"] = "new" });

        Assert.Equal("Services", node["heading"]!.GetValue<string>());
        Assert.Equal("new", node["items"]![0]!["description"]!.GetValue<string>());
        Assert.Equal("Drains", node["items"]![0]!["title"]!.GetValue<string>());
    }
}

public class ClaudeSectionAssistantTests
{
    private sealed class FakeCompletion(string json) : IClaudeJsonCompletion
    {
        public string? LastUser { get; private set; }

        public Task<ClaudeCompletionResult> CompleteAsync(
            string system, string user, IReadOnlyDictionary<string, JsonElement> schema, CancellationToken ct)
        {
            LastUser = user;
            return Task.FromResult(new ClaudeCompletionResult(json, 300, 80));
        }
    }

    private static ClaudeSectionAssistant Assistant(IClaudeJsonCompletion completion) =>
        new(completion, NullLogger<ClaudeSectionAssistant>.Instance);

    [Fact]
    public async Task It_applies_the_revision_and_keeps_type_and_id()
    {
        var section = new HeroSection { Headline = "We fix drains", Subheadline = "Fast" };
        var completion = new FakeCompletion(
            """{ "revisions": [ { "path": "headline", "text": "Blocked drain? We're on it" } ] }""");

        var revised = await Assistant(completion).ReviseAsync(section, "make the headline warmer");

        var hero = Assert.IsType<HeroSection>(revised);
        Assert.Equal("Blocked drain? We're on it", hero.Headline);
        Assert.Equal("Fast", hero.Subheadline); // untouched
        Assert.Equal(section.Id, revised.Id);   // id preserved
    }

    [Fact]
    public async Task Paths_the_model_invents_are_ignored()
    {
        var section = new HeroSection { Headline = "Original" };
        // The model tries to smuggle in a URL change and a made-up field.
        var completion = new FakeCompletion(
            """{ "revisions": [ { "path": "callToActionUrl", "text": "http://evil" }, { "path": "nope", "text": "x" } ] }""");

        var revised = (HeroSection)await Assistant(completion).ReviseAsync(section, "change things");

        Assert.Equal("Original", revised.Headline);
        Assert.NotEqual("http://evil", revised.CallToActionUrl);
    }

    [Fact]
    public async Task Unreadable_output_raises_a_friendly_error()
    {
        var completion = new FakeCompletion("{ not json");

        await Assert.ThrowsAsync<SectionAssistantException>(
            () => Assistant(completion).ReviseAsync(new HeroSection { Headline = "x" }, "do something"));
    }

    [Fact]
    public async Task The_prompt_carries_the_instruction()
    {
        var completion = new FakeCompletion("""{ "revisions": [] }""");

        await Assistant(completion).ReviseAsync(new AboutSection { Heading = "About", Body = "Body" }, "mention we do weddings");

        Assert.Contains("mention we do weddings", completion.LastUser);
    }
}

public class AssistantRateLimiterTests
{
    private static InMemoryAssistantRateLimiter Limiter(int perDay, TimeProvider time) =>
        new(Options.Create(new AssistantOptions { RequestsPerDay = perDay }), time);

    [Fact]
    public void Allows_up_to_the_daily_limit_then_blocks()
    {
        var limiter = Limiter(3, TimeProvider.System);
        var tenant = Guid.NewGuid();

        Assert.True(limiter.TryAcquire(tenant));
        Assert.True(limiter.TryAcquire(tenant));
        Assert.True(limiter.TryAcquire(tenant));
        Assert.False(limiter.TryAcquire(tenant));
    }

    [Fact]
    public void Tenants_have_separate_allowances()
    {
        var limiter = Limiter(1, TimeProvider.System);

        Assert.True(limiter.TryAcquire(Guid.NewGuid()));
        Assert.True(limiter.TryAcquire(Guid.NewGuid()));
    }

    [Fact]
    public void The_window_rolls_forward_after_a_day()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var limiter = Limiter(1, time);
        var tenant = Guid.NewGuid();

        Assert.True(limiter.TryAcquire(tenant));
        Assert.False(limiter.TryAcquire(tenant));

        time.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        Assert.True(limiter.TryAcquire(tenant));
    }

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
