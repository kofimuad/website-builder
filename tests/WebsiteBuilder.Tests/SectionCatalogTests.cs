using Microsoft.Extensions.DependencyInjection;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;

namespace WebsiteBuilder.Tests;

public class SectionCatalogTests
{
    [Fact]
    public void Every_catalog_entry_creates_its_section_type()
    {
        foreach (var entry in SectionCatalog.Entries)
        {
            var section = entry.Create();

            Assert.NotNull(section);
            Assert.NotEqual(Guid.Empty, section.Id);
            Assert.True(section.Visible);
        }
    }

    [Fact]
    public void Catalog_covers_all_eight_section_types()
    {
        var kinds = SectionCatalog.Entries.Select(e => e.Kind).ToHashSet();

        Assert.Equal(
            ["hero", "about", "services", "gallery", "testimonials", "contact", "hoursMap", "cta"],
            kinds);
    }

    [Fact]
    public void Added_sections_survive_a_serialization_round_trip()
    {
        // Adding a section from the picker must produce something the renderer and store accept.
        var definition = new SiteDefinition { Sections = SectionCatalog.Entries.Select(e => e.Create()).ToList() };

        var restored = SiteDefinitionSerializer.Deserialize(SiteDefinitionSerializer.Serialize(definition));

        Assert.Equal(
            definition.Sections.Select(s => s.GetType()),
            restored.Sections.Select(s => s.GetType()));
    }
}

[Collection(nameof(PostgresCollection))]
public class SectionOrderRenderingTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Sections_render_correctly_in_any_order()
    {
        var subdomain = $"o{Guid.NewGuid():N}"[..12];

        using (var scope = _factory.Services.CreateScope())
        {
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

            var tenant = new Tenant { Subdomain = subdomain, Name = "Order Co" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantContext.TenantId = tenant.Id;

            // Deliberately unusual order: CTA first, hero last, contact in the middle.
            var site = new Site
            {
                Name = "Order Co",
                Draft = new SiteDefinition
                {
                    Sections =
                    [
                        new CtaSection { Headline = "ZZZ closing line", ButtonLabel = "Go", ButtonUrl = "#x" },
                        new ContactSection { Heading = "YYY reach us", Email = "a@b.co" },
                        new AboutSection { Heading = "XXX about heading", Body = "Body text." },
                        new HeroSection { Headline = "WWW top headline" },
                    ],
                },
            };
            site.Publish();
            db.Sites.Add(site);
            await db.SaveChangesAsync();
        }

        var html = await _factory.CreateClient().GetStringAsync($"http://{subdomain}.platform.com/");

        Assert.Contains("WWW top headline", html);
        Assert.Contains("XXX about heading", html);
        Assert.Contains("YYY reach us", html);
        Assert.Contains("ZZZ closing line", html);
    }
}
