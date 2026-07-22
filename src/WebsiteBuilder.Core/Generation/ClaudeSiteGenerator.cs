using System.Text.Json;
using Microsoft.Extensions.Logging;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// Generates a site by asking Claude for the copy, validating the result against the schema and
/// the fact guard, and retrying with corrective feedback when either fails. Throws when it cannot
/// produce clean output; a wrapper (see FallbackSiteGenerator) turns that into the template site.
/// </summary>
public sealed class ClaudeSiteGenerator(
    IClaudeJsonCompletion completion,
    ILogger<ClaudeSiteGenerator> logger) : ISiteGenerator
{
    // Claude Opus 4.8 list pricing, US dollars per million tokens.
    private const decimal InputPricePerMillion = 5.00m;
    private const decimal OutputPricePerMillion = 25.00m;

    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<SiteDefinition> GenerateAsync(BusinessProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var schema = SiteGenerationSchema.Build();
        IReadOnlyList<string> lastViolations = [];

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var user = SiteGenerationPrompt.BuildUser(profile, attempt == 1 ? null : lastViolations);

            var result = await completion.CompleteAsync(SiteGenerationPrompt.System, user, schema, cancellationToken);
            LogCost(profile, attempt, result);

            GeneratedSiteContent? content;
            try
            {
                content = JsonSerializer.Deserialize<GeneratedSiteContent>(result.Json, JsonOptions);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Generation attempt {Attempt} for {Business} produced unparsable JSON.", attempt, profile.BusinessName);
                continue;
            }

            if (content is null)
            {
                logger.LogWarning("Generation attempt {Attempt} for {Business} deserialized to null.", attempt, profile.BusinessName);
                continue;
            }

            var violations = GeneratedContentGuard.Check(content, profile);
            if (violations.Count > 0)
            {
                lastViolations = violations;
                logger.LogWarning(
                    "Generation attempt {Attempt} for {Business} was rejected by the fact guard: {Violations}",
                    attempt, profile.BusinessName, string.Join("; ", violations));
                continue;
            }

            return SiteContentAssembler.Assemble(content, profile);
        }

        throw new SiteGenerationException(
            $"Claude did not produce clean site copy for '{profile.BusinessName}' after {MaxAttempts} attempts.");
    }

    private void LogCost(BusinessProfile profile, int attempt, ClaudeCompletionResult result)
    {
        var cost = result.InputTokens / 1_000_000m * InputPricePerMillion
                   + result.OutputTokens / 1_000_000m * OutputPricePerMillion;

        logger.LogInformation(
            "Site generation for {Business} attempt {Attempt}: {InputTokens} in + {OutputTokens} out tokens, ~${Cost} (USD).",
            profile.BusinessName, attempt, result.InputTokens, result.OutputTokens, cost.ToString("0.0000"));
    }
}

public sealed class SiteGenerationException(string message) : Exception(message);
