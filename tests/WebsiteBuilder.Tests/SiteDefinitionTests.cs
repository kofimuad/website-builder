using System.Text.Json;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Tests;

public class SiteDefinitionSerializerTests
{
    private static SiteDefinition SampleWithEverySectionType() => new()
    {
        Meta = new SiteMeta { BusinessName = "Joe's Plumbing", Tagline = "Fast, tidy, fair" },
        Theme = new SiteTheme
        {
            Palette = new ColorPalette { Primary = "#0a7", Text = "#111" },
            Fonts = new FontPair { Heading = "Fraunces", Body = "Inter" },
        },
        Sections =
        [
            new HeroSection { Headline = "Blocked drain?", Subheadline = "We come today", CallToActionLabel = "Call now" },
            new AboutSection { Heading = "About us", Body = "Family run since 1998." },
            new ServicesSection
            {
                Heading = "What we do",
                Items = [new ServiceItem { Title = "Drains", Description = "Cleared fast", PriceLabel = "from GHS 200" }],
            },
            new GallerySection
            {
                Heading = "Our work",
                Images = [new GalleryImage { Url = "/img/1.jpg", AltText = "A repaired sink", Caption = "Sink" }],
            },
            new TestimonialsSection
            {
                Heading = "Reviews",
                Items = [new Testimonial { Quote = "Brilliant", AuthorName = "Ama", AuthorDetail = "Osu" }],
            },
            new ContactSection { Heading = "Get in touch", Email = "joe@example.com", WhatsAppNumber = "+233200000000" },
            new HoursMapSection
            {
                Heading = "Find us",
                AddressLines = ["12 High St", "Accra"],
                MapQuery = "12 High St, Accra",
                OpeningHours =
                [
                    new OpeningHours { Day = DayOfWeek.Monday, Opens = new TimeOnly(9, 0), Closes = new TimeOnly(17, 30) },
                    new OpeningHours { Day = DayOfWeek.Sunday, Closed = true },
                ],
            },
            new CtaSection { Headline = "Ready?", ButtonLabel = "Book now", ButtonUrl = "/contact" },
        ],
    };

    [Fact]
    public void Every_section_type_survives_a_round_trip_with_its_own_fields()
    {
        var original = SampleWithEverySectionType();

        var restored = SiteDefinitionSerializer.Deserialize(SiteDefinitionSerializer.Serialize(original));

        Assert.Equal(original.Sections.Count, restored.Sections.Count);
        Assert.Equal(
            original.Sections.Select(s => s.GetType()),
            restored.Sections.Select(s => s.GetType()));

        Assert.Equal("Blocked drain?", Assert.IsType<HeroSection>(restored.Sections[0]).Headline);
        Assert.Equal("from GHS 200", Assert.IsType<ServicesSection>(restored.Sections[2]).Items[0].PriceLabel);
        Assert.Equal("A repaired sink", Assert.IsType<GallerySection>(restored.Sections[3]).Images[0].AltText);

        var hours = Assert.IsType<HoursMapSection>(restored.Sections[6]);
        Assert.Equal(new TimeOnly(17, 30), hours.OpeningHours[0].Closes);
        Assert.True(hours.OpeningHours[1].Closed);
    }

    [Fact]
    public void Section_order_is_preserved()
    {
        var definition = SampleWithEverySectionType();
        var expected = definition.Sections.Select(s => s.Id).ToList();

        var restored = SiteDefinitionSerializer.Deserialize(SiteDefinitionSerializer.Serialize(definition));

        Assert.Equal(expected, restored.Sections.Select(s => s.Id));
    }

    [Fact]
    public void Theme_is_stored_separately_from_section_content()
    {
        var json = SiteDefinitionSerializer.Serialize(SampleWithEverySectionType());

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("#0a7", root.GetProperty("theme").GetProperty("palette").GetProperty("primary").GetString());
        Assert.Equal("Fraunces", root.GetProperty("theme").GetProperty("fonts").GetProperty("heading").GetString());
        // Sections carry no styling of their own.
        Assert.False(root.GetProperty("sections")[0].TryGetProperty("palette", out _));
    }

    [Fact]
    public void Serializing_stamps_the_current_schema_version()
    {
        var json = SiteDefinitionSerializer.Serialize(new SiteDefinition { SchemaVersion = 0 });

        using var document = JsonDocument.Parse(json);
        Assert.Equal(SiteDefinition.CurrentSchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void A_document_written_before_schema_versioning_is_upgraded_on_read()
    {
        const string legacy = """
            { "meta": { "businessName": "Old Site" }, "sections": [ { "type": "hero", "headline": "Hi" } ] }
            """;

        var restored = SiteDefinitionSerializer.Deserialize(legacy);

        Assert.Equal(SiteDefinition.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Equal("Old Site", restored.Meta.BusinessName);
        Assert.Equal("Hi", Assert.IsType<HeroSection>(restored.Sections[0]).Headline);
    }

    [Fact]
    public void A_document_from_a_newer_build_is_rejected_rather_than_silently_truncated()
    {
        var futureJson = $$"""{ "schemaVersion": {{SiteDefinition.CurrentSchemaVersion + 1}}, "sections": [] }""";

        var exception = Assert.Throws<InvalidSiteDefinitionException>(
            () => SiteDefinitionSerializer.Deserialize(futureJson));

        Assert.Contains("schema version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_default_document_written_by_the_AddSiteDefinitions_migration_is_readable()
    {
        // Backfilled onto rows that existed before the Draft column, so it must stay loadable.
        const string backfilled = """{"meta":{},"theme":{},"sections":[],"schemaVersion":1}""";

        var restored = SiteDefinitionSerializer.Deserialize(backfilled);

        Assert.Empty(restored.Sections);
        Assert.Equal("", restored.Meta.BusinessName);
        Assert.NotNull(restored.Theme.Palette.Primary);
    }

    [Fact]
    public void Sections_load_regardless_of_key_order()
    {
        // Postgres jsonb re-sorts object keys, so the discriminator is not necessarily first.
        const string reordered = """
            {"sections":[{"id":"0f8fad5b-d9cb-469f-a165-70867728950e","type":"hero","visible":true,"headline":"Hi"}],"schemaVersion":1}
            """;

        var restored = SiteDefinitionSerializer.Deserialize(reordered);

        Assert.Equal("Hi", Assert.IsType<HeroSection>(restored.Sections[0]).Headline);
    }

    [Fact]
    public void Unknown_section_types_are_rejected_rather_than_dropped()
    {
        const string json = """{ "schemaVersion": 1, "sections": [ { "type": "notARealSection" } ] }""";

        Assert.ThrowsAny<JsonException>(() => SiteDefinitionSerializer.Deserialize(json));
    }
}

public class SitePublishingTests
{
    private static Site NewSite() => new()
    {
        Name = "Joe's Plumbing",
        TenantId = Guid.NewGuid(),
        Draft = new SiteDefinition
        {
            Meta = new SiteMeta { BusinessName = "Joe's Plumbing" },
            Sections = [new HeroSection { Headline = "Original headline" }],
        },
    };

    [Fact]
    public void A_new_site_is_not_published()
    {
        var site = NewSite();

        Assert.False(site.IsPublished);
        Assert.Null(site.Published);
        Assert.Null(site.PublishedUtc);
    }

    [Fact]
    public void Publishing_copies_the_draft_to_the_live_site()
    {
        var site = NewSite();

        site.Publish();

        Assert.True(site.IsPublished);
        Assert.Equal("Original headline", Assert.IsType<HeroSection>(site.Published!.Sections[0]).Headline);
        Assert.NotNull(site.PublishedUtc);
    }

    [Fact]
    public void Editing_the_draft_after_publishing_does_not_change_the_live_site()
    {
        var site = NewSite();
        site.Publish();

        // Mutating in place is exactly what the editor does, and is what a shallow copy would leak.
        ((HeroSection)site.Draft.Sections[0]).Headline = "Edited headline";
        site.Draft.Sections.Add(new CtaSection { Headline = "New section" });
        site.Draft.Theme.Palette.Primary = "#ff0000";

        var published = site.Published!;
        Assert.Equal("Original headline", Assert.IsType<HeroSection>(published.Sections[0]).Headline);
        Assert.Single(published.Sections);
        Assert.NotEqual("#ff0000", published.Theme.Palette.Primary);
    }

    [Fact]
    public void Publishing_again_promotes_the_newer_draft()
    {
        var site = NewSite();
        site.Publish();

        ((HeroSection)site.Draft.Sections[0]).Headline = "Second version";
        site.Publish();

        Assert.Equal("Second version", Assert.IsType<HeroSection>(site.Published!.Sections[0]).Headline);
    }

    [Fact]
    public void Discarding_the_draft_restores_the_live_content_without_sharing_it()
    {
        var site = NewSite();
        site.Publish();
        ((HeroSection)site.Draft.Sections[0]).Headline = "Unwanted edit";

        site.DiscardDraft();
        Assert.Equal("Original headline", Assert.IsType<HeroSection>(site.Draft.Sections[0]).Headline);

        ((HeroSection)site.Draft.Sections[0]).Headline = "Another edit";
        Assert.Equal("Original headline", Assert.IsType<HeroSection>(site.Published!.Sections[0]).Headline);
    }

    [Fact]
    public void Discarding_the_draft_of_a_never_published_site_throws()
    {
        Assert.Throws<InvalidOperationException>(() => NewSite().DiscardDraft());
    }
}
