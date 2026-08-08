namespace WebsiteBuilder.Core.Tenancy;

/// <summary>Why a web address the owner typed cannot be used. <see cref="None"/> means it can.</summary>
public enum SubdomainProblem
{
    None,
    Empty,
    TooShort,
    TooLong,
    InvalidCharacters,
    EdgeHyphen,
    DoubleHyphen,
    Reserved,
    Taken,
}

/// <summary>
/// The rules for an address an owner picks for themselves, as opposed to one
/// <see cref="Onboarding.SubdomainSuggester"/> derives from their business name. Suggested slugs
/// are valid by construction; typed ones are not, and this is the only place that decides.
///
/// Everything here is decidable without the database. Whether the name is already taken is not,
/// so it lives on the service — but it shares <see cref="SubdomainProblem"/> so the UI has one
/// thing to render.
/// </summary>
public static class SubdomainPolicy
{
    /// <summary>Short enough to be a typo, and single letters are worth keeping back.</summary>
    public const int MinLength = 3;

    /// <summary>Matches what the suggester will produce, well inside the 63-char DNS label limit.</summary>
    public const int MaxLength = 40;

    /// <summary>
    /// Lower-cases and trims. Host names are case-insensitive and
    /// <see cref="HostClassification.Classify"/> lower-cases the incoming host, so a stored
    /// address with a capital in it would never match anything.
    /// </summary>
    public static string Normalize(string? candidate) =>
        candidate?.Trim().ToLowerInvariant() ?? "";

    public static SubdomainProblem Validate(string? candidate, TenantResolutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var value = Normalize(candidate);

        if (value.Length == 0) return SubdomainProblem.Empty;
        if (value.Length < MinLength) return SubdomainProblem.TooShort;
        if (value.Length > MaxLength) return SubdomainProblem.TooLong;

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                return SubdomainProblem.InvalidCharacters;
            }
        }

        // A DNS label may not begin or end with a hyphen.
        if (value[0] == '-' || value[^1] == '-') return SubdomainProblem.EdgeHyphen;

        // Legal in DNS, but "joes--plumbing" reads as a mistake, and `xn--` is the punycode prefix
        // that lets a name render as something else entirely in the address bar.
        if (value.Contains("--", StringComparison.Ordinal)) return SubdomainProblem.DoubleHyphen;

        return options.ReservedSubdomains.Contains(value, StringComparer.OrdinalIgnoreCase)
            ? SubdomainProblem.Reserved
            : SubdomainProblem.None;
    }

    /// <summary>
    /// What to show the owner. These are read by someone who has never heard of DNS, so they say
    /// what to do rather than what is wrong, and never use the word "subdomain".
    /// </summary>
    public static string Describe(SubdomainProblem problem) => problem switch
    {
        SubdomainProblem.None => "",
        SubdomainProblem.Empty => "Choose a web address for your site.",
        SubdomainProblem.TooShort => $"A little longer, please — at least {MinLength} letters.",
        SubdomainProblem.TooLong => $"That's a bit long. Keep it to {MaxLength} letters or fewer.",
        SubdomainProblem.InvalidCharacters => "Use only letters, numbers and hyphens — no spaces.",
        SubdomainProblem.EdgeHyphen => "It can't start or end with a hyphen.",
        SubdomainProblem.DoubleHyphen => "Use just one hyphen between words.",
        SubdomainProblem.Reserved => "That address is kept for the platform. Try another.",
        SubdomainProblem.Taken => "Someone already has that address. Try another.",
        _ => "That address can't be used. Try another.",
    };
}
