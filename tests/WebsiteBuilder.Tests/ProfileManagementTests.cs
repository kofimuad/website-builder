using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Management;
using WebsiteBuilder.Web.Onboarding;

namespace WebsiteBuilder.Tests;

public class ProfileToDraftTests
{
    private static SiteDefinition Draft() => new()
    {
        Meta = new SiteMeta { BusinessName = "Old Name" },
        Sections =
        [
            new HeroSection { Headline = "Hi" },
            new ContactSection { Heading = "Get in touch", PhoneNumber = "old", Email = "old@x" },
        ],
    };

    [Fact]
    public void Contact_details_flow_into_the_contact_section()
    {
        var draft = Draft();
        var profile = new BusinessProfile
        {
            BusinessName = "New Name",
            Category = "plumber",
            PhoneNumber = "+233201111111",
            WhatsAppNumber = "+233202222222",
            Email = "new@example.com",
        };

        ProfileToDraft.Apply(profile, draft);

        var contact = draft.Sections.OfType<ContactSection>().Single();
        Assert.Equal("New Name", draft.Meta.BusinessName);
        Assert.Equal("+233201111111", contact.PhoneNumber);
        Assert.Equal("+233202222222", contact.WhatsAppNumber);
        Assert.Equal("new@example.com", contact.Email);
    }

    [Fact]
    public void An_address_added_later_creates_a_find_us_section_before_contact()
    {
        var draft = Draft();
        var profile = new BusinessProfile
        {
            BusinessName = "Joe",
            Category = "plumber",
            AddressLines = ["12 High Street", "Osu"],
            OpeningHours = [new OpeningHours { Day = DayOfWeek.Monday, Opens = new TimeOnly(9, 0), Closes = new TimeOnly(17, 0) }],
        };

        ProfileToDraft.Apply(profile, draft);

        var hoursIndex = draft.Sections.FindIndex(s => s is HoursMapSection);
        var contactIndex = draft.Sections.FindIndex(s => s is ContactSection);
        Assert.True(hoursIndex >= 0);
        Assert.True(hoursIndex < contactIndex);

        var hours = draft.Sections.OfType<HoursMapSection>().Single();
        Assert.Equal(["12 High Street", "Osu"], hours.AddressLines);
        Assert.Equal("12 High Street, Osu", hours.MapQuery);
        Assert.Single(hours.OpeningHours);
    }

    [Fact]
    public void Existing_hours_section_is_updated_in_place()
    {
        var draft = Draft();
        draft.Sections.Insert(1, new HoursMapSection { Heading = "Find us", AddressLines = ["stale"] });

        var profile = new BusinessProfile { BusinessName = "Joe", Category = "plumber", AddressLines = ["New Street"] };
        ProfileToDraft.Apply(profile, draft);

        var hours = draft.Sections.OfType<HoursMapSection>().Single();
        Assert.Equal(["New Street"], hours.AddressLines);
    }

    [Fact]
    public void With_no_location_info_no_empty_section_is_added()
    {
        var draft = Draft();
        var profile = new BusinessProfile { BusinessName = "Joe", Category = "plumber" };

        ProfileToDraft.Apply(profile, draft);

        Assert.Empty(draft.Sections.OfType<HoursMapSection>());
    }
}

[Collection(nameof(PostgresCollection))]
public class SiteManagementServiceTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private async Task<OnboardingResult> OnboardAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OnboardingService>().CompleteAsync(new BusinessProfile
        {
            BusinessName = name,
            Category = "plumber",
            Offerings = ["Drain clearing"],
            PhoneNumber = "+233200000000",
        });
    }

    [Fact]
    public async Task Loading_by_id_returns_the_profile_and_scopes_the_tenant()
    {
        var result = await OnboardAsync($"Load Co {Guid.NewGuid():N}");

        using var scope = _factory.Services.CreateScope();
        var managed = await scope.ServiceProvider.GetRequiredService<SiteManagementService>().LoadAsync(result.SiteId);

        Assert.NotNull(managed);
        Assert.Equal(result.SiteId, managed!.Site.Id);
        Assert.Equal(result.TenantId, scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId);
    }

    [Fact]
    public async Task Loading_an_unknown_site_returns_null()
    {
        using var scope = _factory.Services.CreateScope();
        var managed = await scope.ServiceProvider.GetRequiredService<SiteManagementService>().LoadAsync(Guid.NewGuid());

        Assert.Null(managed);
    }

    [Fact]
    public async Task Editing_the_profile_changes_the_draft_but_not_the_published_site()
    {
        var result = await OnboardAsync($"Draft Only {Guid.NewGuid():N}");

        // Publish the original so there is a live snapshot to compare against.
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = result.TenantId;
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            (await db.Sites.SingleAsync()).Publish();
            await db.SaveChangesAsync();
        }

        // Edit the phone number and save (draft only).
        using (var scope = _factory.Services.CreateScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<SiteManagementService>();
            var managed = await mgmt.LoadAsync(result.SiteId);
            managed!.Profile.PhoneNumber = "+233209999999";
            await mgmt.SaveProfileAsync(result.SiteId);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = result.TenantId;
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            var site = await db.Sites.SingleAsync();

            Assert.Equal("+233209999999", site.Draft.Sections.OfType<ContactSection>().Single().PhoneNumber);
            Assert.Equal("+233200000000", site.Published!.Sections.OfType<ContactSection>().Single().PhoneNumber);
        }
    }

    [Fact]
    public async Task Publishing_makes_the_edited_details_live()
    {
        var result = await OnboardAsync($"Publish Co {Guid.NewGuid():N}");

        using (var scope = _factory.Services.CreateScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<SiteManagementService>();
            var managed = await mgmt.LoadAsync(result.SiteId);
            managed!.Profile.Email = "fresh@example.com";
            await mgmt.SaveProfileAsync(result.SiteId);
            await mgmt.PublishAsync(result.SiteId);
        }

        var html = await _factory.CreateClient().GetStringAsync($"http://{result.Subdomain}.platform.com/");
        Assert.Contains("fresh@example.com", html);
    }
}
