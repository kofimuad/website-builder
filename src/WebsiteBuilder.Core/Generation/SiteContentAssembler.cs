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
            Sections = SitePlanBuilder.Build(
                CategoryTemplateCatalog.Match(profile.Category),
                profile,
                Copy(content, profile)),
        };
    }

    /// <summary>
    /// Everything the model is allowed to decide, and nothing else. Service titles are the owner's
    /// own words from the profile — the model supplies only the description for each, matched back
    /// by title so a reordered or recased response cannot shuffle them.
    /// </summary>
    private static SiteCopy Copy(GeneratedSiteContent content, BusinessProfile profile) => new(
        HeroHeadline: content.HeroHeadline,
        HeroSubheadline: content.HeroSubheadline,
        AboutBody: content.AboutBody,
        CtaHeadline: FirstNonBlank(content.CtaHeadline, $"Ready to get started with {profile.BusinessName}?"),
        CtaButtonLabel: FirstNonBlank(content.CtaButtonLabel, ContactActions.DefaultLabel(profile.PrimaryAction)),
        AboutHeading: content.AboutHeading,
        ServiceDescriptions: DescriptionsByTitle(content));

    private static Dictionary<string, string> DescriptionsByTitle(GeneratedSiteContent content)
    {
        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var service in content.Services)
        {
            descriptions[service.Title.Trim()] = service.Description;
        }

        return descriptions;
    }

    private static string FirstNonBlank(string preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
