using Microsoft.Extensions.Logging;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// Tries the primary generator (the model) and falls back to the secondary (the deterministic
/// template) if it fails or is cancelled by nothing but its own error. Onboarding must always end
/// with a site, even when the model is slow, unavailable, or over budget.
/// </summary>
public sealed class FallbackSiteGenerator(
    ISiteGenerator primary,
    ISiteGenerator fallback,
    ILogger<FallbackSiteGenerator> logger) : ISiteGenerator
{
    public async Task<SiteDefinition> GenerateAsync(
        BusinessProfile profile,
        IProgress<OnboardingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await primary.GenerateAsync(profile, progress, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A genuine caller cancellation is not a generator failure — don't mask it.
            throw;
        }
        catch (Exception ex)
        {
            // Error, not warning: the site still gets built, so the only symptom anyone sees is
            // unexpectedly generic copy — which reads as "the AI isn't working" with nothing to
            // point at. This line is the whole explanation, so it should be hard to miss.
            logger.LogError(
                ex,
                "AI GENERATION FAILED for {Business} — falling back to the template site. The copy " +
                "on this site was written by the deterministic generator, not the model.",
                profile.BusinessName);
            return await fallback.GenerateAsync(profile, progress, cancellationToken);
        }
    }
}
