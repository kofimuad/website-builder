using System.Text;
using WebsiteBuilder.Core.Onboarding;

namespace WebsiteBuilder.Core.Generation;

/// <summary>Builds the system and user prompts that turn a business profile into site copy.</summary>
public static class SiteGenerationPrompt
{
    /// <summary>
    /// Fixed instructions, safe to cache. The guardrail here is the first line of defence against
    /// invented facts; <see cref="GeneratedContentGuard"/> is the second, and the assembler is the
    /// third (facts are injected from the profile, never read from the model's output).
    /// </summary>
    public const string System =
        """
        You write website copy for small local businesses. You are given a few facts about one
        business and you return marketing copy for its website as JSON.

        Write warm, plain, concrete copy in the business's own voice. Short sentences. No filler,
        no clichés ("nestled in the heart of", "one-stop shop", "we pride ourselves"). Aim the copy
        at the business's own customers, not at other businesses.

        You MUST NOT invent facts. Only the details given to you are true. In particular, never
        state or imply any of the following unless it appears verbatim in the details provided:

        - prices, fees, discounts, or "% off"
        - phone numbers, email addresses, or physical addresses
        - years in business, founding dates, or "since <year>"
        - certifications, licences, awards, accreditations, or memberships
        - staff counts, customer counts, ratings, or any statistic
        - guarantees, warranties, or claims of being "the best" / "number one"

        For services, write one short description for each service you are given, keeping the exact
        title. Do not add, remove, rename, or reorder services. If a service's meaning is unclear,
        describe it in general terms rather than guessing specifics.

        Choose the palette that best fits the requested tone: "friendly" (warm, approachable),
        "professional" (calm, trustworthy), or "bold" (confident, high-energy).
        """;

    public static string BuildUser(BusinessProfile profile, IReadOnlyList<string>? previousViolations = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var builder = new StringBuilder();
        builder.AppendLine("Here are the business details. Everything true about this business is below; nothing else is.");
        builder.AppendLine();
        builder.AppendLine($"Business name: {profile.BusinessName}");
        builder.AppendLine($"What it is: {profile.Category}");
        builder.AppendLine($"Desired tone: {profile.Tone}");

        if (!string.IsNullOrWhiteSpace(profile.ServiceArea))
        {
            builder.AppendLine($"Area served: {profile.ServiceArea}");
        }

        if (profile.Offerings.Count > 0)
        {
            builder.AppendLine("Services (keep each title exactly, write one description each):");
            foreach (var offering in profile.Offerings)
            {
                builder.AppendLine($"  - {offering}");
            }
        }
        else
        {
            builder.AppendLine("Services: none given — return an empty services array.");
        }

        builder.AppendLine();
        builder.AppendLine(
            "Write: a hero headline and subheadline, an about heading and 1-2 short paragraphs, " +
            "a description for each service above, a closing call-to-action headline and button label, " +
            "an SEO title and meta description, a one-line tagline, and a palette choice.");
        builder.AppendLine("Do not write phone numbers, emails, addresses, or prices — those are added separately.");

        if (previousViolations is { Count: > 0 })
        {
            builder.AppendLine();
            builder.AppendLine("Your previous attempt included invented facts that must be removed:");
            foreach (var violation in previousViolations)
            {
                builder.AppendLine($"  - {violation}");
            }
            builder.AppendLine("Rewrite the copy without any of them.");
        }

        return builder.ToString();
    }
}
