using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// Turns a business profile into a first draft site. The Claude-backed implementation is WB-3;
/// this seam exists so onboarding can be finished, tested and demonstrated without it, and so
/// generation always has a deterministic fallback when the model is unavailable.
/// </summary>
public interface ISiteGenerator
{
    /// <summary>
    /// Builds a draft site from the profile. <paramref name="progress"/>, if supplied, receives the
    /// real stages as the work reaches them (see <see cref="OnboardingProgress"/>).
    /// </summary>
    Task<SiteDefinition> GenerateAsync(
        BusinessProfile profile,
        IProgress<OnboardingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
