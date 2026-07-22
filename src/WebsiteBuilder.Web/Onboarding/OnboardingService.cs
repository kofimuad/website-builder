using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.Generation;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;

namespace WebsiteBuilder.Web.Onboarding;

public sealed record OnboardingResult(Guid TenantId, Guid SiteId, string Subdomain);

/// <summary>
/// Turns finished interview answers into a tenant, a stored profile and a first draft site.
/// </summary>
public sealed class OnboardingService(
    WebsiteBuilderDbContext db,
    TenantContext tenantContext,
    ISiteGenerator generator,
    IOptions<TenantResolutionOptions> tenantOptions)
{
    public async Task<OnboardingResult> CompleteAsync(
        BusinessProfile answers,
        IProgress<OnboardingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(answers);

        progress?.Report(OnboardingProgress.Preparing);

        var subdomain = await SubdomainSuggester.SuggestAsync(
            answers.BusinessName,
            tenantOptions.Value,
            (candidate, ct) => db.Tenants.AnyAsync(t => t.Subdomain == candidate, ct),
            cancellationToken);

        var tenant = new Tenant { Subdomain = subdomain, Name = answers.BusinessName };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);

        // Everything below is tenant-owned, so the new tenant has to be in scope before it can
        // be written or read back.
        tenantContext.TenantId = tenant.Id;

        answers.TenantId = tenant.Id;
        answers.UpdatedUtc = DateTimeOffset.UtcNow;
        db.BusinessProfiles.Add(answers);

        var site = new Site
        {
            Name = answers.BusinessName,
            Draft = await generator.GenerateAsync(answers, progress, cancellationToken),
        };
        db.Sites.Add(site);

        progress?.Report(OnboardingProgress.Finishing);
        await db.SaveChangesAsync(cancellationToken);

        return new OnboardingResult(tenant.Id, site.Id, subdomain);
    }
}
