using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WebsiteBuilder.Core.Generation;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Tests;

public class SiteGenerationSchemaTests
{
    [Fact]
    public void The_schema_is_a_valid_object_schema()
    {
        var schema = SiteGenerationSchema.Build();

        Assert.Equal("object", schema["type"].GetString());
        Assert.False(schema["additionalProperties"].GetBoolean());
        Assert.True(schema["properties"].TryGetProperty("palette", out _));
    }
}

public class SiteContentAssemblerTests
{
    private static BusinessProfile Profile() => new()
    {
        BusinessName = "Joe's Plumbing",
        Category = "plumber",
        Offerings = ["Drain clearing", "Leak repair"],
        Tone = BusinessTone.Professional,
        PrimaryAction = PrimaryAction.Call,
        PhoneNumber = "+233200000000",
        Email = "joe@example.com",
        ServiceArea = "Osu",
        AddressLines = ["12 High Street", "Osu, Accra"],
    };

    private static GeneratedSiteContent Content() => new()
    {
        HeroHeadline = "Fast, tidy plumbing",
        HeroSubheadline = "We turn up when we say we will.",
        AboutHeading = "About us",
        AboutBody = "A local plumber you can call on.",
        Services =
        [
            new GeneratedService { Title = "Drain clearing", Description = "Cleared without the mess." },
            new GeneratedService { Title = "Leak repair", Description = "Found and fixed the same visit." },
        ],
        CtaHeadline = "Need a plumber?",
        CtaButtonLabel = "Call us",
        SeoTitle = "Joe's Plumbing — Osu",
        SeoDescription = "Plumbing in Osu.",
        Tagline = "Plumbing done right",
        Palette = "professional",
    };

    [Fact]
    public void Contact_details_come_from_the_profile_not_the_generated_copy()
    {
        var content = Content();
        content.AboutBody = "Call us any time.";

        var site = SiteContentAssembler.Assemble(content, Profile());
        var contact = site.Sections.OfType<ContactSection>().Single();

        Assert.Equal("+233200000000", contact.PhoneNumber);
        Assert.Equal("joe@example.com", contact.Email);
    }

    [Fact]
    public void Service_titles_are_the_owners_words_and_descriptions_are_generated()
    {
        // The model returns titles in a different case and order — the profile's titles must win.
        var content = Content();
        content.Services =
        [
            new GeneratedService { Title = "leak repair", Description = "desc B" },
            new GeneratedService { Title = "DRAIN CLEARING", Description = "desc A" },
        ];

        var services = SiteContentAssembler.Assemble(content, Profile()).Sections.OfType<ServicesSection>().Single();

        Assert.Equal(["Drain clearing", "Leak repair"], services.Items.Select(i => i.Title));
        Assert.Equal("desc A", services.Items[0].Description);
        Assert.Equal("desc B", services.Items[1].Description);
    }

    [Fact]
    public void The_generated_palette_drives_the_theme()
    {
        var content = Content();
        content.Palette = "bold";

        var site = SiteContentAssembler.Assemble(content, Profile());

        Assert.Equal(ThemePresets.For(BusinessTone.Bold).Palette.Primary, site.Theme.Palette.Primary);
    }

    [Fact]
    public void An_unknown_palette_falls_back_to_friendly()
    {
        var content = Content();
        content.Palette = "neon-chaos";

        var site = SiteContentAssembler.Assemble(content, Profile());

        Assert.Equal(ThemePresets.For(BusinessTone.Friendly).Palette.Primary, site.Theme.Palette.Primary);
    }

    [Fact]
    public void Sections_the_profile_cannot_fill_are_omitted()
    {
        var profile = Profile();
        profile.Offerings = [];
        profile.AddressLines = [];

        var site = SiteContentAssembler.Assemble(Content(), profile);

        Assert.Empty(site.Sections.OfType<ServicesSection>());
        Assert.Empty(site.Sections.OfType<HoursMapSection>());
        Assert.Single(site.Sections.OfType<HeroSection>());
        Assert.Single(site.Sections.OfType<ContactSection>());
    }

    [Fact]
    public void Blank_generated_copy_falls_back_rather_than_producing_empty_headings()
    {
        var content = Content();
        content.CtaHeadline = "";
        content.CtaButtonLabel = "";
        content.AboutHeading = "";

        var site = SiteContentAssembler.Assemble(content, Profile());

        Assert.False(string.IsNullOrWhiteSpace(site.Sections.OfType<CtaSection>().Single().Headline));
        Assert.False(string.IsNullOrWhiteSpace(site.Sections.OfType<CtaSection>().Single().ButtonLabel));
        Assert.False(string.IsNullOrWhiteSpace(site.Sections.OfType<AboutSection>().Single().Heading));
    }
}

public class GeneratedContentGuardTests
{
    private static BusinessProfile Profile() => new()
    {
        BusinessName = "Joe's Plumbing",
        Category = "plumber",
        Offerings = ["Drain clearing"],
        PhoneNumber = "+233200000000",
        Email = "joe@example.com",
    };

    private static GeneratedSiteContent Clean() => new()
    {
        HeroHeadline = "Fast, tidy plumbing",
        HeroSubheadline = "We turn up when we say we will.",
        AboutHeading = "About us",
        AboutBody = "A local plumber you can call on.",
        Services = [new GeneratedService { Title = "Drain clearing", Description = "Cleared without the mess." }],
        CtaHeadline = "Need a plumber?",
        CtaButtonLabel = "Call us",
        SeoTitle = "Joe's Plumbing",
        SeoDescription = "Plumbing done right.",
        Tagline = "Plumbing you can trust",
        Palette = "friendly",
    };

    [Fact]
    public void Clean_copy_passes()
    {
        Assert.Empty(GeneratedContentGuard.Check(Clean(), Profile()));
    }

    [Theory]
    [InlineData("Drains cleared from GHS 200.")]
    [InlineData("Only $50 per visit!")]
    [InlineData("Get 20% off your first job.")]
    public void Invented_prices_are_caught(string body)
    {
        var content = Clean();
        content.AboutBody = body;

        Assert.NotEmpty(GeneratedContentGuard.Check(content, Profile()));
    }

    [Fact]
    public void An_invented_phone_number_is_caught()
    {
        var content = Clean();
        content.HeroSubheadline = "Call us on 0300 123 4567 today.";

        var violations = GeneratedContentGuard.Check(content, Profile());

        Assert.Contains(violations, v => v.Contains("phone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_invented_email_is_caught()
    {
        var content = Clean();
        content.CtaHeadline = "Email hello@notjoes.example now.";

        Assert.NotEmpty(GeneratedContentGuard.Check(content, Profile()));
    }

    [Fact]
    public void Repeating_the_profiles_own_phone_number_is_allowed()
    {
        var content = Clean();
        content.HeroSubheadline = "Call us on +233 20 000 0000.";

        Assert.Empty(GeneratedContentGuard.Check(content, Profile()));
    }

    [Fact]
    public void An_invented_price_inside_a_service_description_is_caught()
    {
        var content = Clean();
        content.Services = [new GeneratedService { Title = "Drain clearing", Description = "Cleared for just £40." }];

        Assert.NotEmpty(GeneratedContentGuard.Check(content, Profile()));
    }
}

public class ClaudeSiteGeneratorTests
{
    private sealed class ScriptedCompletion(params string[] responses) : IClaudeJsonCompletion
    {
        private int _index;
        public int Calls { get; private set; }
        public List<string> Prompts { get; } = [];

        public Task<ClaudeCompletionResult> CompleteAsync(
            string system, string user, IReadOnlyDictionary<string, JsonElement> schema, CancellationToken ct)
        {
            Calls++;
            Prompts.Add(user);
            var json = responses[Math.Min(_index++, responses.Length - 1)];
            return Task.FromResult(new ClaudeCompletionResult(json, 1200, 400));
        }
    }

    private sealed class ThrowingCompletion : IClaudeJsonCompletion
    {
        public Task<ClaudeCompletionResult> CompleteAsync(
            string system, string user, IReadOnlyDictionary<string, JsonElement> schema, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("network down");
        }
    }

    private static BusinessProfile Profile() => new()
    {
        BusinessName = "Joe's Plumbing",
        Category = "plumber",
        Offerings = ["Drain clearing"],
        PhoneNumber = "+233200000000",
    };

    private const string GoodJson =
        """
        {
          "heroHeadline": "Fast, tidy plumbing",
          "heroSubheadline": "We turn up when we say we will.",
          "aboutHeading": "About us",
          "aboutBody": "A local plumber you can call on.",
          "services": [{ "title": "Drain clearing", "description": "Cleared without the mess." }],
          "ctaHeadline": "Need a plumber?",
          "ctaButtonLabel": "Call us",
          "seoTitle": "Joe's Plumbing",
          "seoDescription": "Plumbing done right.",
          "tagline": "Plumbing you can trust",
          "palette": "professional"
        }
        """;

    private const string PriceJson =
        """
        {
          "heroHeadline": "Fast, tidy plumbing",
          "heroSubheadline": "Drains cleared from GHS 200.",
          "aboutHeading": "About us",
          "aboutBody": "A local plumber you can call on.",
          "services": [{ "title": "Drain clearing", "description": "Cleared without the mess." }],
          "ctaHeadline": "Need a plumber?",
          "ctaButtonLabel": "Call us",
          "seoTitle": "Joe's Plumbing",
          "seoDescription": "Plumbing done right.",
          "tagline": "Plumbing you can trust",
          "palette": "friendly"
        }
        """;

    private static ClaudeSiteGenerator Generator(IClaudeJsonCompletion completion) =>
        new(completion, NullLogger<ClaudeSiteGenerator>.Instance);

    [Fact]
    public async Task A_clean_response_produces_a_site()
    {
        var completion = new ScriptedCompletion(GoodJson);

        var site = await Generator(completion).GenerateAsync(Profile());

        Assert.Equal("Joe's Plumbing", site.Meta.BusinessName);
        Assert.Equal(1, completion.Calls);
    }

    [Fact]
    public async Task Malformed_json_is_retried()
    {
        var completion = new ScriptedCompletion("{ not json", GoodJson);

        var site = await Generator(completion).GenerateAsync(Profile());

        Assert.Equal(2, completion.Calls);
        Assert.NotEmpty(site.Sections);
    }

    [Fact]
    public async Task A_guard_violation_is_retried_with_corrective_feedback()
    {
        var completion = new ScriptedCompletion(PriceJson, GoodJson);

        await Generator(completion).GenerateAsync(Profile());

        Assert.Equal(2, completion.Calls);
        // The retry prompt tells the model what to remove.
        Assert.Contains("invented facts", completion.Prompts[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task It_gives_up_after_repeated_bad_output()
    {
        var completion = new ScriptedCompletion(PriceJson);

        await Assert.ThrowsAsync<SiteGenerationException>(() => Generator(completion).GenerateAsync(Profile()));
        Assert.Equal(3, completion.Calls);
    }

    [Fact]
    public async Task The_fallback_generator_uses_the_template_when_claude_fails()
    {
        var fallback = new FallbackSiteGenerator(
            primary: Generator(new ThrowingCompletion()),
            fallback: new TemplateSiteGenerator(),
            logger: NullLogger<FallbackSiteGenerator>.Instance);

        var site = await fallback.GenerateAsync(Profile());

        // The template still produces a usable site.
        Assert.NotEmpty(site.Sections);
        Assert.Equal("Joe's Plumbing", site.Meta.BusinessName);
    }

    [Fact]
    public async Task A_caller_cancellation_is_not_swallowed_by_the_fallback()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var fallback = new FallbackSiteGenerator(
            primary: Generator(new ThrowingCompletion()),
            fallback: new TemplateSiteGenerator(),
            logger: NullLogger<FallbackSiteGenerator>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => fallback.GenerateAsync(Profile(), progress: null, cts.Token));
    }
}
