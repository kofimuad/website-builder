using WebsiteBuilder.Core.Generation;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Tests;

public class CategoryTemplateMatchingTests
{
    [Theory]
    // What people actually type, taken from the onboarding suggestions and the way the target
    // market describes itself. The match runs on the owner's own words, not on a picked option.
    [InlineData("restaurant", "restaurant")]
    [InlineData("Chop bar", "restaurant")]
    [InlineData("local bakery", "restaurant")]
    [InlineData("I run a small café", "restaurant")]
    [InlineData("Cooking", "restaurant")]
    [InlineData("cook", "restaurant")]
    [InlineData("chef", "restaurant")]
    [InlineData("Kebab stand", "restaurant")]
    [InlineData("barber", "salon")]
    [InlineData("Hair and nails", "salon")]
    [InlineData("Salon & spa", "salon")]
    [InlineData("plumber", "trades")]
    [InlineData("Plumbing", "trades")]
    [InlineData("electrician", "trades")]
    [InlineData("Cleaning", "trades")]
    [InlineData("borehole drilling", "trades")]
    [InlineData("business consultant", "consultant")]
    [InlineData("bookkeeping services", "consultant")]
    [InlineData("church", "church")]
    [InlineData("youth ministry", "church")]
    [InlineData("NGO", "church")]
    [InlineData("event planner", "events")]
    [InlineData("Wedding decor", "events")]
    [InlineData("photographer", "events")]
    public void The_owners_own_words_choose_a_template(string category, string expectedId)
    {
        Assert.Equal(expectedId, CategoryTemplateCatalog.Match(category).Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("shoe shop")]
    [InlineData("something nobody has thought of")]
    public void Anything_unrecognised_still_gets_a_finished_page(string? category)
    {
        // A miss is expected to be common. It must not produce a worse site than we shipped before
        // categories existed, which is what the general template is.
        Assert.Equal("general", CategoryTemplateCatalog.Match(category).Id);
    }

    [Fact]
    public void The_longest_keyword_wins_so_the_order_of_the_catalog_does_not_matter()
    {
        // "cleaning" (trades) and "consult" (consultant) both match; the longer one decides, and
        // it would decide the same way if the catalog were listed in the opposite order.
        Assert.Equal("trades", CategoryTemplateCatalog.Match("cleaning consultant").Id);
    }

    [Theory]
    // Substring matching would put a mango seller behind a stained-glass window and a spare-parts
    // dealer in a hair salon. Whole-word keywords are what stop it.
    [InlineData("mango stand", "general")]
    [InlineData("spare parts", "general")]
    [InlineData("bandsaw sharpening", "general")]
    [InlineData("chair", "general")]
    public void A_keyword_buried_inside_another_word_does_not_match(string category, string expectedId)
    {
        Assert.Equal(expectedId, CategoryTemplateCatalog.Match(category).Id);
    }

    [Theory]
    // Stems are the other half of the rule: one keyword has to cover how people actually inflect.
    [InlineData("plumber", "trades")]
    [InlineData("plumbing services", "trades")]
    [InlineData("bakery", "restaurant")]
    [InlineData("I am a baker", "restaurant")]
    [InlineData("hairdresser", "salon")]
    public void A_stem_keyword_covers_the_forms_of_a_word(string category, string expectedId)
    {
        Assert.Equal(expectedId, CategoryTemplateCatalog.Match(category).Id);
    }

    [Fact]
    public void No_keyword_belongs_to_two_categories()
    {
        // Identical keywords in two templates would make the winner depend on list order.
        var duplicates = CategoryTemplateCatalog.Templates
            .SelectMany(t => t.Keywords.Select(k => (Keyword: k, t.Id)))
            .GroupBy(pair => pair.Keyword)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(p => p.Id))}")
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Keywords_are_lower_case_because_matching_is_ordinal()
    {
        var wrong = CategoryTemplateCatalog.Templates
            .SelectMany(t => t.Keywords)
            .Where(keyword => keyword != keyword.ToLowerInvariant())
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void Every_suggestion_chip_lands_on_the_category_that_offered_it()
    {
        // Onboarding renders these. A chip that matched a different template — or nothing — would
        // hand the owner a page for someone else's business, from a button we put in front of them.
        foreach (var template in CategoryTemplateCatalog.Templates.Where(t => t.Example is not null))
        {
            Assert.Equal(template.Id, CategoryTemplateCatalog.Match(template.Example).Id);
        }
    }

    [Fact]
    public void Every_category_worth_typing_offers_a_chip()
    {
        var missing = CategoryTemplateCatalog.Templates
            .Where(t => t.Keywords.Count > 0 && string.IsNullOrWhiteSpace(t.Example))
            .Select(t => t.Id);

        Assert.Empty(missing);
    }

    [Fact]
    public void The_launch_categories_are_all_present()
    {
        // WB-45 names these six plus the fallback. A rename here is a product decision.
        Assert.Equal(
            ["restaurant", "salon", "trades", "consultant", "church", "events", "general"],
            CategoryTemplateCatalog.Templates.Select(t => t.Id));
    }
}

public class CategoryTemplateLibraryTests
{
    private static BusinessProfile FullProfile() => new()
    {
        BusinessName = "Joe's Plumbing",
        Category = "plumber",
        Offerings = ["Drain clearing", "Leak repair"],
        PhoneNumber = "+233200000000",
        AddressLines = ["12 High Street", "Osu, Accra"],
    };

    /// <summary>
    /// The acceptance criterion "adding a category is data work, not code changes" only holds if
    /// nothing downstream special-cases a template. Every template in the catalog is built here,
    /// so a new entry that needed code would fail this rather than fail in production.
    /// </summary>
    [Fact]
    public void Every_template_in_the_catalog_builds_a_page()
    {
        foreach (var template in CategoryTemplateCatalog.Templates)
        {
            var sections = SitePlanBuilder.Build(template, FullProfile(), Copy());

            Assert.NotEmpty(sections);
            Assert.Single(sections.OfType<HeroSection>());
            Assert.Single(sections.OfType<ContactSection>());
        }
    }

    [Fact]
    public void Every_slot_names_a_section_the_editor_also_knows()
    {
        var known = CategoryTemplateCatalog.Templates
            .SelectMany(t => t.Lineup)
            .Select(slot => slot.Kind)
            .Distinct()
            .Except(SectionCatalog.Entries.Select(e => e.Kind))
            .ToList();

        Assert.Empty(known);
    }

    [Fact]
    public void An_unknown_section_kind_fails_loudly_rather_than_being_skipped()
    {
        var broken = new CategoryTemplate("broken", "Broken", null, [], [new SectionSlot("carousel", "Nope")]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => SitePlanBuilder.Build(broken, FullProfile(), Copy()));

        Assert.Contains("carousel", exception.Message);
    }

    [Fact]
    public void Every_stock_photo_has_alt_text()
    {
        var photos = CategoryTemplateCatalog.Templates.SelectMany(AllPhotos);

        Assert.All(photos, photo => Assert.False(string.IsNullOrWhiteSpace(photo.AltText)));
    }

    [Fact]
    public void Stock_photos_are_requested_at_the_width_of_their_slot()
    {
        // A hero and a gallery thumbnail downloading the same file is the difference between a
        // page that opens on mobile data and one that does not.
        var restaurant = CategoryTemplateCatalog.ById("restaurant");
        var sections = SitePlanBuilder.Build(restaurant, FullProfile(), Copy());

        var hero = sections.OfType<HeroSection>().Single();
        var gallery = sections.OfType<GallerySection>().Single();

        Assert.Contains("w=1600&h=900", hero.ImageUrl);
        Assert.All(gallery.Images, image => Assert.Contains("w=800&h=600", image.Url));
    }

    [Fact]
    public void The_fallback_template_carries_no_stock_photography()
    {
        // It used to open on a rack of clothes in a boutique. That is the most specific-looking
        // image in the catalog and it was served to every business whose own words matched none of
        // the six categories — which the catalog itself says will be often. A wrong photograph
        // above the fold is worse than no photograph, so this one has none.
        var fallback = CategoryTemplateCatalog.Fallback;

        Assert.Null(fallback.HeroPhoto);
        Assert.Null(fallback.AboutPhoto);
        Assert.Empty(fallback.Gallery);
    }

    [Fact]
    public void The_matched_categories_all_keep_their_hero_photo()
    {
        // The counterpart to the test above: dropping the fallback's photo must not quietly become
        // dropping everyone's. A chop bar getting a plate of food has matched, and that is earned.
        var matched = CategoryTemplateCatalog.Templates
            .Where(t => t.Id != CategoryTemplateCatalog.Fallback.Id);

        Assert.All(matched, template => Assert.NotNull(template.HeroPhoto));
    }

    [Fact]
    public void An_unmatched_business_gets_a_hero_with_no_image_rather_than_a_wrong_one()
    {
        var sections = SitePlanBuilder.Build(
            CategoryTemplateCatalog.Fallback, FullProfile(), Copy());

        Assert.True(string.IsNullOrWhiteSpace(sections.OfType<HeroSection>().Single().ImageUrl));
    }

    [Fact]
    public void An_uploaded_photo_still_fills_the_fallback_hero()
    {
        // Removing the stock photo must not remove the slot: the owner's own picture is exactly
        // what is supposed to go there, and it already outranks stock everywhere else.
        var profile = FullProfile();
        profile.PhotoUrls = ["https://res.cloudinary.com/demo/image/upload/v1/mine.jpg"];

        var sections = SitePlanBuilder.Build(CategoryTemplateCatalog.Fallback, profile, Copy());

        Assert.Equal(profile.PhotoUrls[0], sections.OfType<HeroSection>().Single().ImageUrl);
    }

    [Fact]
    public void No_lineup_asks_for_reviews_because_quotes_cannot_be_pre_filled()
    {
        var lineups = CategoryTemplateCatalog.Templates.SelectMany(t => t.Lineup);

        Assert.DoesNotContain(lineups, slot => slot.Kind == "testimonials");
    }

    private static IEnumerable<StockPhoto> AllPhotos(CategoryTemplate template)
    {
        if (template.HeroPhoto is not null)
        {
            yield return template.HeroPhoto;
        }

        if (template.AboutPhoto is not null)
        {
            yield return template.AboutPhoto;
        }

        foreach (var photo in template.Gallery)
        {
            yield return photo;
        }
    }

    private static SiteCopy Copy() => new(
        HeroHeadline: "Headline",
        HeroSubheadline: "Subheadline",
        AboutBody: "About body",
        CtaHeadline: "Closing line",
        CtaButtonLabel: "Call us");
}

public class CategorySpecificSiteTests
{
    private static BusinessProfile Profile(string category) => new()
    {
        BusinessName = "Auntie Akos Kitchen",
        Category = category,
        Offerings = ["Jollof and chicken", "Banku and tilapia"],
        PhoneNumber = "+233200000000",
        AddressLines = ["12 High Street", "Osu, Accra"],
    };

    private static async Task<SiteDefinition> Generate(string category) =>
        await new TemplateSiteGenerator().GenerateAsync(Profile(category));

    [Fact]
    public async Task A_restaurant_gets_a_menu_rather_than_a_list_of_services()
    {
        var site = await Generate("chop bar");

        Assert.Equal("Our menu", site.Sections.OfType<ServicesSection>().Single().Heading);
    }

    [Fact]
    public async Task A_restaurant_leads_with_the_menu_and_the_food()
    {
        var site = await Generate("restaurant");

        var kinds = site.Sections.Select(s => s.GetType().Name).ToList();

        Assert.Equal(
            ["HeroSection", "ServicesSection", "GallerySection", "AboutSection", "HoursMapSection", "ContactSection", "CtaSection"],
            kinds);
    }

    [Fact]
    public async Task A_salon_asks_for_a_booking_and_a_plumber_asks_for_a_quote()
    {
        var salon = await Generate("hair salon");
        var trades = await Generate("plumber");

        Assert.Equal("Book an appointment", salon.Sections.OfType<ContactSection>().Single().Heading);
        Assert.Equal("Get a quote", trades.Sections.OfType<ContactSection>().Single().Heading);
    }

    [Fact]
    public async Task A_business_with_no_photos_still_gets_a_gallery()
    {
        // The whole point of the stock library: at onboarding nobody has uploaded anything.
        var site = await Generate("barber");
        var gallery = site.Sections.OfType<GallerySection>().Single();

        Assert.NotEmpty(gallery.Images);
        Assert.All(gallery.Images, image => Assert.StartsWith("https://images.unsplash.com/", image.Url));
    }

    [Fact]
    public async Task A_consultant_gets_no_gallery_because_there_is_nothing_honest_to_show()
    {
        var site = await Generate("business consultant");

        Assert.Empty(site.Sections.OfType<GallerySection>());
        Assert.Equal("How I can help", site.Sections.OfType<ServicesSection>().Single().Heading);
    }

    [Fact]
    public async Task The_owners_own_photos_replace_the_stock_ones()
    {
        var profile = Profile("restaurant");
        profile.PhotoUrls = ["https://res.cloudinary.com/demo/image/upload/v1/sites/a/photo.jpg"];

        var site = await new TemplateSiteGenerator().GenerateAsync(profile);
        var gallery = site.Sections.OfType<GallerySection>().Single();

        Assert.Equal(profile.PhotoUrls, gallery.Images.Select(i => i.Url));
        Assert.DoesNotContain(gallery.Images, image => image.Url.Contains("unsplash"));
    }

    [Fact]
    public async Task The_first_uploaded_photo_becomes_the_hero()
    {
        var profile = Profile("restaurant");
        profile.PhotoUrls = ["https://res.cloudinary.com/demo/image/upload/v1/first.jpg", "https://x/second.jpg"];

        var site = await new TemplateSiteGenerator().GenerateAsync(profile);

        Assert.Equal(profile.PhotoUrls[0], site.Sections.OfType<HeroSection>().Single().ImageUrl);
    }

    [Fact]
    public async Task A_category_with_no_gallery_still_shows_photos_the_owner_uploaded()
    {
        // The consultant lineup has no gallery on purpose. Having asked for photos and been given
        // them, dropping them would be the worse of the two surprises.
        var profile = Profile("business consultant");
        profile.PhotoUrls = ["https://res.cloudinary.com/demo/image/upload/v1/work.jpg"];

        var site = await new TemplateSiteGenerator().GenerateAsync(profile);
        var sections = site.Sections;

        var gallery = sections.OfType<GallerySection>().Single();
        Assert.Equal(profile.PhotoUrls, gallery.Images.Select(i => i.Url));

        // Placed before the contact section, so the page still ends by asking for the enquiry.
        Assert.True(sections.IndexOf(gallery) < sections.FindIndex(s => s is ContactSection));
    }

    [Fact]
    public async Task An_owners_photo_carries_no_invented_alt_text()
    {
        var profile = Profile("barber");
        profile.PhotoUrls = ["https://res.cloudinary.com/demo/image/upload/v1/cut.jpg"];

        var site = await new TemplateSiteGenerator().GenerateAsync(profile);

        // Only they know what is in it, and a screen reader would read a guess aloud as fact.
        Assert.All(site.Sections.OfType<GallerySection>().Single().Images,
            image => Assert.Equal("", image.AltText));
    }

    [Fact]
    public async Task The_hero_carries_a_photo_so_the_page_does_not_open_on_flat_colour()
    {
        var site = await Generate("restaurant");

        Assert.False(string.IsNullOrWhiteSpace(site.Sections.OfType<HeroSection>().Single().ImageUrl));
    }

    [Fact]
    public async Task Sections_the_profile_cannot_fill_are_still_omitted()
    {
        var profile = Profile("restaurant");
        profile.Offerings = [];
        profile.AddressLines = [];

        var site = await new TemplateSiteGenerator().GenerateAsync(profile);

        Assert.Empty(site.Sections.OfType<ServicesSection>());
        Assert.Empty(site.Sections.OfType<HoursMapSection>());
        // The gallery survives: its content comes from the library, not from the owner.
        Assert.Single(site.Sections.OfType<GallerySection>());
    }

    [Fact]
    public async Task Both_generators_lay_out_the_same_page_for_the_same_business()
    {
        // The two used to build their own lineups independently, which is how they drifted. The
        // model decides the words; the category decides the page, whichever generator ran.
        var profile = Profile("restaurant");

        var fromTemplate = await new TemplateSiteGenerator().GenerateAsync(profile);
        var fromModel = SiteContentAssembler.Assemble(new GeneratedSiteContent
        {
            HeroHeadline = "Jollof worth the queue",
            HeroSubheadline = "Cooked to order, every day.",
            AboutHeading = "",
            AboutBody = "A kitchen in Osu.",
            Services =
            [
                new GeneratedService { Title = "Jollof and chicken", Description = "Smoky and hot." },
                new GeneratedService { Title = "Banku and tilapia", Description = "Grilled to order." },
            ],
            CtaHeadline = "Hungry?",
            CtaButtonLabel = "Call us",
            SeoTitle = "Auntie Akos Kitchen",
            SeoDescription = "Food in Osu.",
            Tagline = "Home cooking",
            Palette = "friendly",
        }, profile);

        Assert.Equal(
            fromTemplate.Sections.Select(s => s.GetType().Name),
            fromModel.Sections.Select(s => s.GetType().Name));

        Assert.Equal(
            fromTemplate.Sections.OfType<ServicesSection>().Single().Heading,
            fromModel.Sections.OfType<ServicesSection>().Single().Heading);
    }

    [Fact]
    public void The_model_may_name_its_own_about_section()
    {
        var content = new GeneratedSiteContent
        {
            HeroHeadline = "H",
            HeroSubheadline = "S",
            AboutHeading = "Twenty years on this street",
            AboutBody = "B",
            Services = [],
            CtaHeadline = "C",
            CtaButtonLabel = "Call us",
            SeoTitle = "T",
            SeoDescription = "D",
            Tagline = "G",
            Palette = "friendly",
        };

        var site = SiteContentAssembler.Assemble(content, Profile("restaurant"));

        Assert.Equal("Twenty years on this street", site.Sections.OfType<AboutSection>().Single().Heading);
    }
}
