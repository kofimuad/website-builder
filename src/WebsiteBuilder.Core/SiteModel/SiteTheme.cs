namespace WebsiteBuilder.Core.SiteModel;

/// <summary>
/// Presentation settings, kept separate from content so a business can restyle its site
/// without the generator or editor touching a single word of copy.
/// </summary>
public sealed class SiteTheme
{
    public ColorPalette Palette { get; set; } = new();
    public FontPair Fonts { get; set; } = new();
}

public sealed class ColorPalette
{
    public string Primary { get; set; } = "#1f5eff";
    public string Accent { get; set; } = "#ff8a3d";
    public string Background { get; set; } = "#ffffff";
    public string Surface { get; set; } = "#f5f6f8";
    public string Text { get; set; } = "#16161a";
    public string MutedText { get; set; } = "#5c5c66";
}

public sealed class FontPair
{
    public string Heading { get; set; } = "Inter";
    public string Body { get; set; } = "Inter";
}
