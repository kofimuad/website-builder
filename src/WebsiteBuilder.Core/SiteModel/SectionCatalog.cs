namespace WebsiteBuilder.Core.SiteModel;

/// <summary>One addable section type: how to describe it in the picker and how to create a default.</summary>
public sealed record SectionCatalogEntry(
    string Kind,
    string Label,
    string Description,
    Func<SiteSection> Create);

/// <summary>
/// The section types the owner can add from the picker, with a sensible pre-filled default for
/// each. The picker is driven entirely by this list, so a new section type appears in the picker
/// by adding an entry here — no picker code changes.
/// </summary>
public static class SectionCatalog
{
    public static IReadOnlyList<SectionCatalogEntry> Entries { get; } =
    [
        new("hero", "Hero", "A big headline and a button at the top of the page.",
            () => new HeroSection { Headline = "Welcome", Subheadline = "", CallToActionLabel = "Get in touch", CallToActionUrl = "#contact" }),

        new("about", "About", "A heading and a short paragraph about your business.",
            () => new AboutSection { Heading = "About us", Body = "" }),

        new("services", "Services", "A list of what you offer.",
            () => new ServicesSection { Heading = "What we do", Items = [new ServiceItem { Title = "New service", Description = "" }] }),

        new("gallery", "Gallery", "A grid of photos of your work.",
            () => new GallerySection { Heading = "Our work", Images = [] }),

        new("testimonials", "Reviews", "Quotes from happy customers.",
            () => new TestimonialsSection { Heading = "What people say", Items = [new Testimonial { Quote = "", AuthorName = "" }] }),

        new("contact", "Contact", "Ways for customers to reach you.",
            () => new ContactSection { Heading = "Get in touch" }),

        new("hoursMap", "Hours & map", "Your address and opening times.",
            () => new HoursMapSection { Heading = "Find us" }),

        new("cta", "Call to action", "A closing prompt with a button.",
            () => new CtaSection { Headline = "Ready to get started?", ButtonLabel = "Get in touch", ButtonUrl = "#contact" }),
    ];
}
