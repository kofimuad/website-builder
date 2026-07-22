using System.Text.RegularExpressions;
using WebsiteBuilder.Core.Onboarding;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// Second line of defence for the "no invented facts" guardrail: scans generated prose for the
/// fact-shaped tokens the model is told to avoid (prices, phone numbers, emails). Anything that
/// also appears in the profile is allowed — repeating a real detail is fine, inventing one is not.
/// </summary>
public static partial class GeneratedContentGuard
{
    [GeneratedRegex(@"[$£€₵]\s?\d|(?i:\b(?:GHS|USD|EUR|GBP|NGN|KES|ZAR|cedis?|naira|dollars?|pounds?|euros?)\b\s?\d)|\d\s?(?i:%|percent)\s?(?i:off|discount)", RegexOptions.CultureInvariant)]
    private static partial Regex PriceRegex();

    [GeneratedRegex(@"\+?\d[\d\s().\-]{5,}\d", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"[\w.+\-]+@[\w\-]+\.\w+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    /// <summary>Returns a human-readable description of each invented fact found, or an empty list.</summary>
    public static IReadOnlyList<string> Check(GeneratedSiteContent content, BusinessProfile profile)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(profile);

        var known = KnownFacts(profile);
        var violations = new List<string>();

        foreach (var (field, text) in ProseFields(content))
        {
            Scan(field, text, PriceRegex(), "a price or discount", known, violations);
            Scan(field, text, PhoneRegex(), "a phone number", known, violations);
            Scan(field, text, EmailRegex(), "an email address", known, violations);
        }

        return violations;
    }

    private static void Scan(
        string field,
        string text,
        Regex pattern,
        string label,
        string known,
        List<string> violations)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (Match match in pattern.Matches(text))
        {
            // Repeating a detail the owner actually gave us is fine; inventing one is not.
            if (known.Contains(match.Value, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A long digit run (a phone number) that is part of a known number is allowed even if
            // formatted differently. Kept to 6+ digits so a stray price digit isn't waved through
            // just because it appears somewhere in the profile's phone number.
            var matchDigits = DigitsOf(match.Value);
            if (matchDigits.Length >= 6 && DigitsOf(known).Contains(matchDigits, StringComparison.Ordinal))
            {
                continue;
            }

            violations.Add($"{field} contains {label}: \"{match.Value.Trim()}\"");
        }
    }

    private static IEnumerable<(string Field, string Text)> ProseFields(GeneratedSiteContent c)
    {
        yield return (nameof(c.HeroHeadline), c.HeroHeadline);
        yield return (nameof(c.HeroSubheadline), c.HeroSubheadline);
        yield return (nameof(c.AboutHeading), c.AboutHeading);
        yield return (nameof(c.AboutBody), c.AboutBody);
        yield return (nameof(c.CtaHeadline), c.CtaHeadline);
        yield return (nameof(c.CtaButtonLabel), c.CtaButtonLabel);
        yield return (nameof(c.Tagline), c.Tagline);
        yield return (nameof(c.SeoTitle), c.SeoTitle);
        yield return (nameof(c.SeoDescription), c.SeoDescription);

        foreach (var service in c.Services)
        {
            yield return ($"Service '{service.Title}'", service.Description);
        }
    }

    private static string KnownFacts(BusinessProfile profile) => string.Join(
        " ",
        new[]
        {
            profile.BusinessName,
            profile.Category,
            profile.ServiceArea ?? "",
            profile.PhoneNumber ?? "",
            profile.WhatsAppNumber ?? "",
            profile.Email ?? "",
        }
        .Concat(profile.Offerings)
        .Concat(profile.AddressLines));

    private static string DigitsOf(string value) => new(value.Where(char.IsDigit).ToArray());
}
