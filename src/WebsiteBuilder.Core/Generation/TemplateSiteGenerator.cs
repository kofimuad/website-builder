using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// Builds a site from the profile using fixed copy patterns — no model call, no network, same
/// output every time. Claude replaces the wording in WB-3; the section choices made here are the
/// structure that generation is expected to produce.
/// </summary>
public sealed class TemplateSiteGenerator : ISiteGenerator
{
    public Task<SiteDefinition> GenerateAsync(BusinessProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var definition = new SiteDefinition
        {
            Meta = BuildMeta(profile),
            Theme = BuildTheme(profile.Tone),
            Sections = BuildSections(profile),
        };

        return Task.FromResult(definition);
    }

    private static SiteMeta BuildMeta(BusinessProfile profile)
    {
        var where = string.IsNullOrWhiteSpace(profile.ServiceArea) ? null : profile.ServiceArea;

        return new SiteMeta
        {
            BusinessName = profile.BusinessName,
            Tagline = where is null ? profile.Category : $"{profile.Category} in {where}",
            SeoTitle = where is null
                ? $"{profile.BusinessName} — {profile.Category}"
                : $"{profile.BusinessName} — {profile.Category} in {where}",
            SeoDescription = BuildDescription(profile),
        };
    }

    private static string BuildDescription(BusinessProfile profile)
    {
        var offerings = profile.Offerings.Count > 0
            ? string.Join(", ", profile.Offerings.Take(3))
            : profile.Category;

        var where = string.IsNullOrWhiteSpace(profile.ServiceArea) ? "" : $" in {profile.ServiceArea}";

        return $"{profile.BusinessName} offers {offerings}{where}. {ContactActions.DefaultLabel(profile.PrimaryAction)}.";
    }

    private static SiteTheme BuildTheme(BusinessTone tone) => ThemePresets.For(tone);

    private static List<SiteSection> BuildSections(BusinessProfile profile)
    {
        var sections = new List<SiteSection>
        {
            new HeroSection
            {
                Headline = BuildHeadline(profile),
                Subheadline = BuildSubheadline(profile),
                CallToActionLabel = ContactActions.DefaultLabel(profile.PrimaryAction),
                CallToActionUrl = ContactActions.ResolveUrl(profile),
            },
            new AboutSection
            {
                Heading = $"About {profile.BusinessName}",
                Body = BuildAbout(profile),
            },
        };

        // Only offer sections the profile can actually fill: an empty heading with nothing under
        // it looks broken, and the owner would have to delete it by hand.
        if (profile.Offerings.Count > 0)
        {
            sections.Add(new ServicesSection
            {
                Heading = "What we do",
                Items = profile.Offerings
                    .Select(offering => new ServiceItem { Title = offering, Description = "" })
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
            Headline = BuildClosingLine(profile),
            ButtonLabel = ContactActions.DefaultLabel(profile.PrimaryAction),
            ButtonUrl = ContactActions.ResolveUrl(profile),
        });

        return sections;
    }

    private static string BuildHeadline(BusinessProfile profile) => profile.Tone switch
    {
        BusinessTone.Professional => profile.BusinessName,
        BusinessTone.Bold => $"{profile.Category} done properly.",
        _ => $"Welcome to {profile.BusinessName}",
    };

    private static string BuildSubheadline(BusinessProfile profile)
    {
        var where = string.IsNullOrWhiteSpace(profile.ServiceArea) ? "" : $" in {profile.ServiceArea}";
        return $"{Capitalise(profile.Category)}{where}.";
    }

    private static string BuildAbout(BusinessProfile profile)
    {
        var where = string.IsNullOrWhiteSpace(profile.ServiceArea)
            ? "."
            : $", serving {profile.ServiceArea}.";

        var offerings = profile.Offerings.Count > 0
            ? $"\nWe help with {string.Join(", ", profile.Offerings)}."
            : "";

        return $"{profile.BusinessName} is a {profile.Category}{where}{offerings}";
    }

    private static string BuildClosingLine(BusinessProfile profile) => profile.PrimaryAction switch
    {
        PrimaryAction.Visit => $"Come and see us at {profile.BusinessName}.",
        PrimaryAction.Book => "Ready to book?",
        PrimaryAction.Message => "Send us a message.",
        _ => $"Need a {profile.Category}?",
    };

    private static string Capitalise(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
