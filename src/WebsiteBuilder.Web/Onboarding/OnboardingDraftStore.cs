using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using WebsiteBuilder.Core.Onboarding;

namespace WebsiteBuilder.Web.Onboarding;

/// <summary>
/// Holds finished interview answers across the sign-in round trip (WB-15). The interview is
/// anonymous, so by the time we know who the owner is the answers must have survived a redirect to
/// Google and back, or a hop to the visitor's email client and back.
///
/// Server-side, keyed by a token carried in the return URL. Not a cookie: an interactive Blazor
/// circuit has no live response to write one on. Not the answers themselves in the URL either —
/// free-text offerings and an address outgrow a query string quickly.
///
/// Deliberately in memory. A restart or a second instance loses a pending draft, and the cost is
/// re-answering seven questions, not lost customer data — nothing is persisted until sign-in
/// completes. A table would buy durability for something designed to live minutes.
/// </summary>
public sealed class OnboardingDraftStore(IMemoryCache cache)
{
    /// <summary>Long enough to read an email on another device, short enough to be forgotten.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public string Stash(BusinessProfile answers)
    {
        // Random rather than sequential: the key is the only thing standing between a pending
        // interview and someone else claiming those answers as their own site.
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        cache.Set(CacheKey(key), answers, Lifetime);
        return key;
    }

    /// <summary>Takes the answers and removes them: a stashed interview is redeemed exactly once.</summary>
    public BusinessProfile? Take(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || !cache.TryGetValue(CacheKey(key), out BusinessProfile? answers))
        {
            return null;
        }

        cache.Remove(CacheKey(key));
        return answers;
    }

    private static string CacheKey(string key) => $"onboarding-draft:{key}";
}
