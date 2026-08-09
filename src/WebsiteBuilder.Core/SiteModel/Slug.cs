using System.Buffers;
using System.Globalization;
using System.Text;

namespace WebsiteBuilder.Core.SiteModel;

/// <summary>
/// Turns a name someone typed into something safe to put in a URL.
/// <para>
/// Shared by subdomains and product addresses. The rules about what makes an <em>acceptable</em>
/// slug differ between the two — a subdomain has a minimum length and a reserved list, a product
/// only has to be unique within its tenant — but the normalisation is the same, and having two
/// copies of accent folding would mean two behaviours the day one is fixed.
/// </para>
/// </summary>
public static class Slug
{
    private static readonly SearchValues<char> Apostrophes = SearchValues.Create("'’ʼ`´");

    /// <summary>
    /// Accents are folded to their base letter so "Café Ámà" becomes "cafe-ama". Returns an empty
    /// string when nothing usable is left, which the caller decides what to do about.
    /// </summary>
    public static string From(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue; // The accent, now separated from the letter it sat on.
            }

            if (Apostrophes.Contains(character))
            {
                continue; // "Joe's" should read joes, not joe-s.
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');

        return slug.Length > maxLength ? slug[..maxLength].TrimEnd('-') : slug;
    }
}
