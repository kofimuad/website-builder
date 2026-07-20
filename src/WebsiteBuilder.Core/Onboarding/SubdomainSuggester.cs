using System.Buffers;
using System.Globalization;
using System.Text;
using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Core.Onboarding;

/// <summary>Turns a business name into the address its site will live at.</summary>
public static class SubdomainSuggester
{
    private const int MaxLength = 40;
    private const string Fallback = "my-site";

    private static readonly SearchValues<char> Apostrophes = SearchValues.Create("'’ʼ`´");

    /// <summary>
    /// Best-effort slug of a business name. Accents are folded to their base letter so "Café Ámà"
    /// becomes "cafe-ama"; a name with nothing ASCII left falls back rather than producing an
    /// empty or unusable host name.
    /// </summary>
    public static string Slugify(string? businessName)
    {
        if (string.IsNullOrWhiteSpace(businessName))
        {
            return Fallback;
        }

        var decomposed = businessName.Normalize(NormalizationForm.FormD);
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

        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
        }

        return slug.Length == 0 ? Fallback : slug;
    }

    /// <summary>
    /// First free address based on the business name, trying "name", "name-2", "name-3" and so on.
    /// Reserved subdomains are treated as taken.
    /// </summary>
    public static async Task<string> SuggestAsync(
        string? businessName,
        TenantResolutionOptions options,
        Func<string, CancellationToken, Task<bool>> isTaken,
        CancellationToken cancellationToken = default)
    {
        var baseSlug = Slugify(businessName);

        for (var attempt = 1; ; attempt++)
        {
            var candidate = attempt == 1 ? baseSlug : $"{baseSlug}-{attempt}";

            var reserved = options.ReservedSubdomains.Contains(candidate, StringComparer.OrdinalIgnoreCase);
            if (!reserved && !await isTaken(candidate, cancellationToken))
            {
                return candidate;
            }
        }
    }
}
