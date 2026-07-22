using System.Globalization;

namespace WebsiteBuilder.Core.SiteModel;

/// <summary>WCAG relative-luminance contrast, used to prove the theme presets meet AA.</summary>
public static class Wcag
{
    /// <summary>AA minimum contrast for normal-size text.</summary>
    public const double AaNormal = 4.5;

    /// <summary>Contrast ratio (1–21) between two "#rrggbb" colours.</summary>
    public static double ContrastRatio(string hexA, string hexB)
    {
        var la = RelativeLuminance(hexA);
        var lb = RelativeLuminance(hexB);
        var (hi, lo) = la >= lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var (r, g, b) = Parse(hex);
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static double Channel(double srgb) =>
        srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);

    private static (double R, double G, double B) Parse(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length != 6)
        {
            throw new ArgumentException($"Expected a #rrggbb colour, got '{hex}'.", nameof(hex));
        }

        double Component(int start) =>
            int.Parse(s.Substring(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;

        return (Component(0), Component(2), Component(4));
    }
}
