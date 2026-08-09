namespace WebsiteBuilder.Core.SiteModel;

/// <summary>A font the platform ships, with the file to load it from and the stack to fall back to.</summary>
public sealed record WebFont(string Family, string FileName, string Fallback);

/// <summary>
/// The fonts a generated site may load.
/// <para>
/// This exists because a theme's font name is data — it comes out of a jsonb document that a
/// previous version of the app wrote — and turning arbitrary data into a font request would be
/// both a correctness problem and an injection one. A name in this list is served from our own
/// origin; anything else falls through to the system stack, which is exactly what every site did
/// before webfonts existed and still renders fine.
/// </para>
/// <para>
/// Self-hosted rather than fetched from Google: a published site is one document with no blocking
/// third-party requests, and on a Ghanaian mobile connection a second DNS lookup and TLS handshake
/// costs more than the font does. Both files are the latin subset of a variable font — one request
/// covers every weight the design uses.
/// </para>
/// </summary>
public static class WebFontCatalog
{
    /// <summary>Warm, high-contrast serif for headings. SIL Open Font License.</summary>
    public static WebFont Fraunces { get; } = new("Fraunces", "fraunces-latin-var.woff2", "Georgia, serif");

    /// <summary>Body text. Built for small sizes on low-density screens. SIL Open Font License.</summary>
    public static WebFont Inter { get; } = new("Inter", "inter-latin-var.woff2", "system-ui, sans-serif");

    public static IReadOnlyList<WebFont> Fonts { get; } = [Fraunces, Inter];

    /// <summary>The shipped font with this family name, or null if we do not host it.</summary>
    public static WebFont? Find(string? family) =>
        Fonts.FirstOrDefault(f => string.Equals(f.Family, family?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The CSS font stack for a theme's font name. A hosted family is quoted and backed by its own
    /// fallback; anything else is passed through as the owner's chosen system font.
    /// <para>
    /// The result is written into a <c>&lt;style&gt;</c> block unescaped — it has to be, because a
    /// CSS parser reads <c>&amp;quot;</c> as four literal characters and throws the declaration
    /// away. So the name is stripped here to letters, digits, spaces and hyphens: a font family
    /// never needs anything else, and the value arrives from a jsonb document that some earlier
    /// version of this app wrote.
    /// </para>
    /// </summary>
    public static string StackFor(string? family)
    {
        var hosted = Find(family);
        if (hosted is not null)
        {
            return $"\"{hosted.Family}\", {hosted.Fallback}";
        }

        var safe = Sanitise(family);

        return string.IsNullOrEmpty(safe)
            ? "system-ui, sans-serif"
            : $"\"{safe}\", system-ui, sans-serif";
    }

    private static string Sanitise(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return "";
        }

        var kept = family.Trim()
            .Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-')
            .ToArray();

        return new string(kept).Trim();
    }

    /// <summary>The distinct hosted fonts a theme actually uses, so a page loads no file it will not draw with.</summary>
    public static IEnumerable<WebFont> Used(SiteTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        return new[] { Find(theme.Fonts.Heading), Find(theme.Fonts.Body) }
            .OfType<WebFont>()
            .DistinctBy(f => f.Family);
    }
}
