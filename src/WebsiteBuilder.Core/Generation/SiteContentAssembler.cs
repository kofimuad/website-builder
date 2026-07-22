using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// Combines the model's prose with the profile's facts into a site definition. Every fact —
/// contact details, address, service area, service titles, section ordering — comes from the
/// profile; the model supplies only copy. This is what makes the "no invented facts" guarantee
/// structural rather than trust-based.
/// </summary>
public static class SiteContentAssembler
{
    public static SiteDefinition Assemble(GeneratedSiteContent content, BusinessProfile profile)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(profile);

        return new SiteDefinition
        {
            Meta = new SiteMeta
            {
                BusinessName = profile.BusinessName,
                Tagline = NullIfBlank(content.Tagline),
                SeoTitle = NullIfBlank(content.SeoTitle),
                SeoDescription = NullIfBlank(content.SeoDescription),
            },
            Theme = ThemePresets.For(ThemePresets.ParsePalette(content.Palette)),
            Sections = BuildSections(content, profile),
        };
    }

    private static List<SiteSection> BuildSections(GeneratedSiteContent content, BusinessProfile profile)
    {
        var sections = new List<SiteSection>
        {
            new HeroSection
            {
                Headline = content.HeroHeadline,
                Subheadline = content.HeroSubheadline,
                CallToActionLabel = FirstNonBlank(content.CtaButtonLabel, ContactActions.DefaultLabel(profile.PrimaryAction)),
                CallToActionUrl = ContactActions.ResolveUrl(profile),
            },
            new AboutSection
            {
                Heading = FirstNonBlank(content.AboutHeading, $"About {profile.BusinessName}"),
                Body = content.AboutBody,
            },
        };

        // Titles are the owner's own words, taken from the profile — never from the model.
        // The model only supplies the description for each.
        if (profile.Offerings.Count > 0)
        {
            sections.Add(new ServicesSection
            {
                Heading = "What we do",
                Items = profile.Offerings
                    .Select(title => new ServiceItem { Title = title, Description = DescriptionFor(title, content) })
                    .ToList(),
            });
        }

        if (profile.AddressLines.Count > 0)
        {
            sections.Add(new HoursMapSection
            {
                Heading = "Find us",
                AddressLines = [.. profile.AddressLines],
                MapQuery = string.Join(", ", profile.AddressLines),
            });
        }

        sections.Add(new ContactSection
        {
            Heading = "Get in touch",
            PhoneNumber = profile.PhoneNumber,
            WhatsAppNumber = profile.WhatsAppNumber,
            Email = profile.Email,
        });

        sections.Add(new CtaSection
        {
            Headline = FirstNonBlank(content.CtaHeadline, $"Ready to get started with {profile.BusinessName}?"),
            ButtonLabel = FirstNonBlank(content.CtaButtonLabel, ContactActions.DefaultLabel(profile.PrimaryAction)),
            ButtonUrl = ContactActions.ResolveUrl(profile),
        });

        return sections;
    }

    /// <summary>Matches the model's description to a title, preferring an exact title match then position.</summary>
    private static string DescriptionFor(string title, GeneratedSiteContent content)
    {
        var byTitle = content.Services
            .FirstOrDefault(s => string.Equals(s.Title.Trim(), title.Trim(), StringComparison.OrdinalIgnoreCase));

        return byTitle?.Description ?? "";
    }

    private static string FirstNonBlank(string preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
