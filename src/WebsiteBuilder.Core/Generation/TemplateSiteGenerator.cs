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

        return $"{profile.BusinessName} offers {offerings}{where}. {CallToAction(profile.PrimaryAction)}.";
    }

    private static SiteTheme BuildTheme(BusinessTone tone) => tone switch
    {
        BusinessTone.Professional => new SiteTheme
        {
            Palette = new ColorPalette
            {
                Primary = "#1f3a68",
                Accent = "#3f7cac",
                Background = "#ffffff",
                Surface = "#f3f5f9",
                Text = "#14181f",
                MutedText = "#59606d",
            },
            Fonts = new FontPair { Heading = "Georgia", Body = "system-ui" },
        },

        BusinessTone.Bold => new SiteTheme
        {
            Palette = new ColorPalette
            {
                Primary = "#d4341f",
                Accent = "#1d1d1f",
                Background = "#ffffff",
                Surface = "#fdf0ed",
                Text = "#16100f",
                MutedText = "#6b5c59",
            },
            Fonts = new FontPair { Heading = "Impact", Body = "system-ui" },
        },

        _ => new SiteTheme
        {
            Palette = new ColorPalette
            {
                Primary = "#0a7d55",
                Accent = "#f2a900",
                Background = "#ffffff",
                Surface = "#f1f7f4",
                Text = "#15211c",
                MutedText = "#5a6b64",
            },
            Fonts = new FontPair { Heading = "system-ui", Body = "system-ui" },
        },
    };

    private static List<SiteSection> BuildSections(BusinessProfile profile)
    {
        var sections = new List<SiteSection>
        {
            new HeroSection
            {
                Headline = BuildHeadline(profile),
                Subheadline = BuildSubheadline(profile),
                CallToActionLabel = CallToAction(profile.PrimaryAction),
                CallToActionUrl = ActionUrl(profile),
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
            ButtonLabel = CallToAction(profile.PrimaryAction),
            ButtonUrl = ActionUrl(profile),
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

    private static string CallToAction(PrimaryAction action) => action switch
    {
        PrimaryAction.Visit => "Get directions",
        PrimaryAction.Book => "Book now",
        PrimaryAction.Message => "Message us",
        _ => "Call us",
    };

    /// <summary>Falls back through whatever contact details exist, so the button is never dead.</summary>
    private static string ActionUrl(BusinessProfile profile)
    {
        var phone = profile.PhoneNumber;
        var whatsApp = profile.WhatsAppNumber;

        return profile.PrimaryAction switch
        {
            PrimaryAction.Message when !string.IsNullOrWhiteSpace(whatsApp) => WhatsAppUrl(whatsApp),
            PrimaryAction.Visit when profile.AddressLines.Count > 0 =>
                "https://maps.google.com/maps?q=" + Uri.EscapeDataString(string.Join(", ", profile.AddressLines)),
            _ when !string.IsNullOrWhiteSpace(phone) => $"tel:{phone}",
            _ when !string.IsNullOrWhiteSpace(whatsApp) => WhatsAppUrl(whatsApp),
            _ when !string.IsNullOrWhiteSpace(profile.Email) => $"mailto:{profile.Email}",
            _ => "#contact",
        };
    }

    private static string WhatsAppUrl(string number) => $"https://wa.me/{number.TrimStart('+')}";

    private static string Capitalise(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
