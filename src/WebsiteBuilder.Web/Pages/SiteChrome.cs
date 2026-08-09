using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Web.Pages;

/// <summary>
/// Everything the shared page furniture needs, worked out once from the published definition:
/// the theme's font stacks, the nav, and the contact details behind the call bar.
/// <para>
/// It exists because the shop pages are the same website as the home page. They must carry the
/// same header, the same footer and the same call bar, and computing that in four Razor files
/// would guarantee the four drift apart.
/// </para>
/// </summary>
public sealed class SiteChrome
{
    public SiteChrome(SiteDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Definition = definition;
        Visible = definition.Sections.Where(s => s.Visible).ToList();

        Anchors = BuildAnchors(Visible);
        NavLinks = BuildNavLinks(Visible, Anchors, definition.Meta.BusinessName);

        Hero = Visible.OfType<HeroSection>().FirstOrDefault();
        Contact = Visible.OfType<ContactSection>().FirstOrDefault();

        HeadingStack = WebFontCatalog.StackFor(definition.Theme.Fonts.Heading);
        BodyStack = WebFontCatalog.StackFor(definition.Theme.Fonts.Body);
    }

    public SiteDefinition Definition { get; }
    public IReadOnlyList<SiteSection> Visible { get; }
    public IReadOnlyDictionary<SiteSection, string> Anchors { get; }
    public IReadOnlyList<(string Anchor, string Label)> NavLinks { get; }
    public HeroSection? Hero { get; }
    public ContactSection? Contact { get; }
    public string HeadingStack { get; }
    public string BodyStack { get; }

    public SiteTheme Theme => Definition.Theme;
    public string BusinessName => Definition.Meta.BusinessName;

    public bool HasCallBar => Contact is not null
        && (!string.IsNullOrWhiteSpace(Contact.PhoneNumber) || !string.IsNullOrWhiteSpace(Contact.WhatsAppNumber));

    /// <summary>The WhatsApp number in wa.me form, or null when the business has not given one.</summary>
    public string? WhatsAppLink => string.IsNullOrWhiteSpace(Contact?.WhatsAppNumber)
        ? null
        : $"https://wa.me/{Contact.WhatsAppNumber.TrimStart('+')}";

    public bool HasShop => Visible.OfType<ShopSection>().Any();

    /// <summary>
    /// One anchor per section, so the nav can link to it and a repeated section type cannot
    /// produce two elements with the same id.
    /// </summary>
    private static Dictionary<SiteSection, string> BuildAnchors(IReadOnlyList<SiteSection> visible)
    {
        var anchors = new Dictionary<SiteSection, string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var section in visible)
        {
            var kind = section switch
            {
                AboutSection => "about",
                ShopSection => "shop",
                ServicesSection => "services",
                GallerySection => "gallery",
                TestimonialsSection => "reviews",
                ContactSection => "contact",
                HoursMapSection => "find-us",
                CtaSection => "cta",
                _ => "top",
            };

            seen[kind] = seen.TryGetValue(kind, out var count) ? count + 1 : 1;
            anchors[section] = seen[kind] == 1 ? kind : $"{kind}-{seen[kind]}";
        }

        return anchors;
    }

    /// <summary>Sections named by their own heading, so a restaurant's nav says "Our menu".</summary>
    private static List<(string Anchor, string Label)> BuildNavLinks(
        IReadOnlyList<SiteSection> visible,
        IReadOnlyDictionary<SiteSection, string> anchors,
        string businessName) =>
        visible
            .Select(s => (Anchor: anchors[s], Label: s switch
            {
                ShopSection shop => shop.Heading,
                ServicesSection services => services.Heading,
                GallerySection gallery => gallery.Heading,
                // "About Joe's Plumbing" is right on the page and far too long in a nav bar.
                AboutSection about => about.Heading.Contains(businessName, StringComparison.OrdinalIgnoreCase)
                    ? "About"
                    : about.Heading,
                HoursMapSection hours => hours.Heading,
                ContactSection contact => contact.Heading,
                _ => null,
            }))
            .Where(l => !string.IsNullOrWhiteSpace(l.Label))
            .Select(l => (l.Anchor, Label: l.Label!))
            .Take(4)
            .ToList();
}
