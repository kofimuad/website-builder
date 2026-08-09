using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Core.Onboarding;

/// <summary>Turns a business name into the address its site will live at.</summary>
public static class SubdomainSuggester
{
    private const int MaxLength = 40;
    private const string Fallback = "my-site";

    /// <summary>
    /// Best-effort slug of a business name. Accents are folded to their base letter so "Café Ámà"
    /// becomes "cafe-ama"; a name with nothing ASCII left falls back rather than producing an
    /// empty or unusable host name.
    /// </summary>
    public static string Slugify(string? businessName)
    {
        var slug = Slug.From(businessName, MaxLength);

        // Anything this produces must survive SubdomainPolicy, or a name auto-assigned at
        // onboarding would be rejected at that same owner's first publish. One- and two-letter
        // addresses are held back deliberately, so a name that short falls back instead — and the
        // owner picks a real address before going live anyway.
        return slug.Length < SubdomainPolicy.MinLength ? Fallback : slug;
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
