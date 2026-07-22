using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// The curated look for each business tone. Kept in one place so the template generator, the
/// Claude generator, and (later) the palette picker all share the same WCAG-safe combinations.
/// </summary>
public static class ThemePresets
{
    public static SiteTheme For(BusinessTone tone) => tone switch
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

    /// <summary>Maps a palette name (as chosen during generation) to a tone; unknown names fall back to friendly.</summary>
    public static BusinessTone ParsePalette(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "professional" => BusinessTone.Professional,
        "bold" => BusinessTone.Bold,
        _ => BusinessTone.Friendly,
    };
}
