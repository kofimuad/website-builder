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
    Task<SiteDefinition> GenerateAsync(BusinessProfile profile, CancellationToken cancellationToken = default);
}
