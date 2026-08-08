using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// Builds a site from the profile using fixed copy patterns — no model call, no network, same
/// output every time. Only the wording is decided here: which sections a page has, in what order,
/// under what headings, and with which photographs all come from the business category's template
/// via <see cref="SitePlanBuilder"/>, exactly as they do on the model path.
/// </summary>
public sealed class TemplateSiteGenerator : ISiteGenerator
{
    public Task<SiteDefinition> GenerateAsync(
        BusinessProfile profile,
        IProgress<OnboardingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        progress?.Report(OnboardingProgress.WritingCopy);
        progress?.Report(OnboardingProgress.BuildingPages);

        return Task.FromResult(Generate(profile));
    }

    /// <summary>
    /// The generator without the asynchronous wrapper. There is no I/O here — the async signature
    /// exists for <see cref="ISiteGenerator"/>, not for this implementation — and the onboarding
    /// preview needs to rebuild a site on every keystroke, where blocking on a Task would be a
    /// deadlock waiting to happen inside a Blazor circuit.
    /// </summary>
    public SiteDefinition Generate(BusinessProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new SiteDefinition
        {
            Meta = BuildMeta(profile),
            Theme = BuildTheme(profile.Tone),
            Sections = SitePlanBuilder.Build(CategoryTemplateCatalog.Match(profile.Category), profile, Copy(profile)),
        };
    }

    /// <summary>
    /// Fixed copy patterns. No service descriptions: the template has nothing to say about
    /// "Drain clearing" that the title does not already say, and inventing something would be
    /// worse than the blank the owner can fill in.
    /// </summary>
    private static SiteCopy Copy(BusinessProfile profile) => new(
        HeroHeadline: BuildHeadline(profile),
        HeroSubheadline: BuildSubheadline(profile),
        AboutBody: BuildAbout(profile),
        CtaHeadline: BuildClosingLine(profile),
        CtaButtonLabel: ContactActions.DefaultLabel(profile.PrimaryAction));

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
