namespace WebsiteBuilder.Core.SiteModel;

/// <summary>A named, curated look: one palette + font pairing the owner can apply with one tap.</summary>
public sealed record ThemePreset(string Id, string Label, SiteTheme Theme);

/// <summary>
/// The curated set of looks. Every preset is verified WCAG AA on the section templates (see the
/// preset tests), so the owner can only ever pick a combination that stays readable — there is no
/// free-form colour or font input anywhere in the MVP.
/// </summary>
public static class ThemePresetCatalog
{
    public static IReadOnlyList<ThemePreset> Presets { get; } =
    [
        Preset("evergreen", "Evergreen", "#0a6b49", "#f2a900", "#f1f7f4", "#15211c", "#4f5b55", "system-ui", "system-ui"),
        Preset("navy", "Navy", "#1f3a68", "#3f7cac", "#f3f5f9", "#14181f", "#525966", "Georgia", "system-ui"),
        Preset("ember", "Ember", "#a82f1a", "#1d1d1f", "#fdf0ed", "#16100f", "#66574f", "Impact", "system-ui"),
        Preset("teal", "Teal", "#0f6461", "#e07a3b", "#eef6f6", "#14201f", "#4d5a59", "Georgia", "system-ui"),
        Preset("plum", "Plum", "#6a2f6b", "#c85a94", "#f7f0f7", "#1e141f", "#5a505d", "Palatino Linotype", "system-ui"),
        Preset("slate", "Slate", "#33435a", "#6b8299", "#f2f4f7", "#161b22", "#525a66", "Trebuchet MS", "system-ui"),
        Preset("forest", "Forest", "#276031", "#c79232", "#f0f5f0", "#15211a", "#4f5b52", "Georgia", "system-ui"),
        Preset("ocean", "Ocean", "#125273", "#3f92bd", "#eef4f8", "#14202a", "#4d5964", "Trebuchet MS", "system-ui"),
        Preset("rust", "Rust", "#8f3a1d", "#d98b43", "#fbf1ea", "#1c130e", "#63544b", "Palatino Linotype", "system-ui"),
        Preset("berry", "Berry", "#8a2846", "#c25b7a", "#fbeff3", "#1f1015", "#5f545a", "Georgia", "system-ui"),
    ];

    public static ThemePreset ById(string id) =>
        Presets.FirstOrDefault(p => p.Id == id) ?? Presets[0];

    private static ThemePreset Preset(
        string id, string label, string primary, string accent, string surface,
        string text, string muted, string heading, string body) =>
        new(id, label, new SiteTheme
        {
            Palette = new ColorPalette
            {
                Primary = primary,
                Accent = accent,
                Background = "#ffffff",
                Surface = surface,
                Text = text,
                MutedText = muted,
            },
            Fonts = new FontPair { Heading = heading, Body = body },
        });
}
