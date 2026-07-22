using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// The curated look for each business tone. Kept in one place so the template generator, the
/// Claude generator, and (later) the palette picker all share the same WCAG-safe combinations.
/// </summary>
public static class ThemePresets
{
    /// <summary>The curated preset each tone starts from. Drawn from the shared, AA-verified catalog.</summary>
    public static SiteTheme For(BusinessTone tone) => ThemePresetCatalog.ById(PresetIdFor(tone)).Theme.Clone();

    private static string PresetIdFor(BusinessTone tone) => tone switch
    {
        BusinessTone.Professional => "navy",
        BusinessTone.Bold => "ember",
        _ => "evergreen",
    };

    /// <summary>Maps a palette name (as chosen during generation) to a tone; unknown names fall back to friendly.</summary>
    public static BusinessTone ParsePalette(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "professional" => BusinessTone.Professional,
        "bold" => BusinessTone.Bold,
        _ => BusinessTone.Friendly,
    };
}
