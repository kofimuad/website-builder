using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebsiteBuilder.Core.Generation;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Onboarding;

namespace WebsiteBuilder.Tests;

public class TemplateSiteGeneratorTests
{
    private static readonly TemplateSiteGenerator Generator = new();

    private static BusinessProfile Profile() => new()
    {
        BusinessName = "Joe's Plumbing",
        Category = "plumber",
        Offerings = ["Drain clearing", "Leak repair"],
        Tone = BusinessTone.Friendly,
        PrimaryAction = PrimaryAction.Call,
        PhoneNumber = "+233200000000",
        ServiceArea = "Osu",
    };

    [Fact]
    public async Task The_generated_site_uses_the_answers_given()
    {
        var site = await Generator.GenerateAsync(Profile());

        Assert.Equal("Joe's Plumbing", site.Meta.BusinessName);
        Assert.Contains("Osu", site.Meta.SeoTitle);

        var services = site.Sections.OfType<ServicesSection>().Single();
        Assert.Equal(["Drain clearing", "Leak repair"], services.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task Sections_the_profile_cannot_fill_are_left_out()
    {
        var profile = Profile();
        profile.Offerings = [];
        profile.AddressLines = [];

        var site = await Generator.GenerateAsync(profile);

        Assert.Empty(site.Sections.OfType<ServicesSection>());
        Assert.Empty(site.Sections.OfType<HoursMapSection>());
        // The parts that always have something to say survive.
        Assert.Single(site.Sections.OfType<HeroSection>());
        Assert.Single(site.Sections.OfType<ContactSection>());
    }

    [Fact]
    public async Task An_address_produces_a_find_us_section()
    {
        var profile = Profile();
        profile.AddressLines = ["12 High Street", "Osu, Accra"];

        var site = await Generator.GenerateAsync(profile);

        var hours = site.Sections.OfType<HoursMapSection>().Single();
        Assert.Equal(["12 High Street", "Osu, Accra"], hours.AddressLines);
        Assert.Contains("Osu", hours.MapQuery);
    }

    [Theory]
    [InlineData(BusinessTone.Friendly)]
    [InlineData(BusinessTone.Professional)]
    [InlineData(BusinessTone.Bold)]
    public async Task Each_tone_produces_its_own_look(BusinessTone tone)
    {
        var profile = Profile();
        profile.Tone = tone;

        var site = await Generator.GenerateAsync(profile);

        Assert.Matches("^#[0-9a-f]{6}$", site.Theme.Palette.Primary);
    }

    [Fact]
    public async Task Tones_do_not_all_look_the_same()
    {
        var palettes = new List<string>();

        foreach (var tone in Enum.GetValues<BusinessTone>())
        {
            var profile = Profile();
            profile.Tone = tone;
            palettes.Add((await Generator.GenerateAsync(profile)).Theme.Palette.Primary);
        }

        Assert.Equal(palettes.Count, palettes.Distinct().Count());
    }

    [Fact]
    public async Task The_call_to_action_button_is_never_dead()
    {
        // Whatever the owner chose, the button has to point at something they actually gave us.
        foreach (var action in Enum.GetValues<PrimaryAction>())
        {
            var profile = Profile();
            profile.PrimaryAction = action;
            profile.PhoneNumber = null;
            profile.Email = "joe@example.com";

            var site = await Generator.GenerateAsync(profile);
            var hero = site.Sections.OfType<HeroSection>().Single();

            Assert.False(string.IsNullOrWhiteSpace(hero.CallToActionUrl));
            Assert.Contains("joe@example.com", hero.CallToActionUrl);
        }
    }

    [Fact]
    public async Task Generation_is_deterministic()
    {
        var first = await Generator.GenerateAsync(Profile());
        var second = await Generator.GenerateAsync(Profile());

        // Section ids are new each time; the content must not be.
        Assert.Equal(
            first.Sections.Select(s => s.GetType().Name),
            second.Sections.Select(s => s.GetType().Name));
        Assert.Equal(first.Meta.SeoDescription, second.Meta.SeoDescription);
    }
}

[Collection(nameof(PostgresCollection))]
public class OnboardingServiceTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private static BusinessProfile Answers(string businessName = "Joe's Plumbing") => new()
    {
        BusinessName = businessName,
        Category = "plumber",
        Offerings = ["Drain clearing"],
        PhoneNumber = "+233200000000",
        ServiceArea = "Osu",
    };

    private async Task<OnboardingResult> CompleteAsync(BusinessProfile answers)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OnboardingService>().CompleteAsync(answers);
    }

    [Fact]
    public async Task Finishing_the_interview_creates_a_tenant_a_profile_and_a_draft_site()
    {
        var result = await CompleteAsync(Answers());

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = result.TenantId;
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

        Assert.Equal("joes-plumbing", result.Subdomain);
        Assert.Equal("Joe's Plumbing", (await db.BusinessProfiles.SingleAsync()).BusinessName);

        var site = await db.Sites.SingleAsync();
        Assert.NotEmpty(site.Draft.Sections);
    }

    [Fact]
    public async Task A_new_site_is_not_published_until_the_owner_says_so()
    {
        var result = await CompleteAsync(Answers());

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = result.TenantId;
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

        var site = await db.Sites.SingleAsync();
        Assert.False(site.IsPublished);
    }

    [Fact]
    public async Task A_second_business_with_the_same_name_gets_its_own_address()
    {
        var first = await CompleteAsync(Answers("Copy Cats"));
        var second = await CompleteAsync(Answers("Copy Cats"));

        Assert.Equal("copy-cats", first.Subdomain);
        Assert.Equal("copy-cats-2", second.Subdomain);
    }

    [Fact]
    public async Task The_profile_belongs_to_the_new_tenant_only()
    {
        var first = await CompleteAsync(Answers("First Business"));
        await CompleteAsync(Answers("Second Business"));

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = first.TenantId;
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

        var profile = Assert.Single(await db.BusinessProfiles.ToListAsync());
        Assert.Equal("First Business", profile.BusinessName);
    }

    [Fact]
    public async Task The_new_site_is_reachable_at_the_suggested_address_once_published()
    {
        var result = await CompleteAsync(Answers("Reachable Co"));

        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = result.TenantId;
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            var site = await db.Sites.SingleAsync();
            site.Publish();
            await db.SaveChangesAsync();
        }

        var html = await _factory.CreateClient().GetStringAsync($"http://{result.Subdomain}.platform.com/");

        Assert.Contains("Reachable Co", html);
    }
}
