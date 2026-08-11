using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using WebsiteBuilder.Core.Onboarding;

namespace WebsiteBuilder.Web.Onboarding;

/// <summary>
/// Holds the answers behind the onboarding live preview, so the preview can be rendered by the
/// <em>real</em> site renderer instead of a hand-drawn mock.
///
/// <para>
/// It exists because of a boundary: the renderer is a Razor Page partial and needs an
/// <c>HttpContext</c>, while the wizard is an interactive Blazor circuit with no live response to
/// render into. The preview is therefore an iframe pointing at a page, and that page needs to read
/// answers that have not been saved anywhere — the visitor is anonymous and no site row exists
/// until they sign in.
/// </para>
///
/// <para>
/// Deliberately close to <see cref="OnboardingDraftStore"/>, with one difference that matters:
/// this one is <b>read repeatedly and not consumed</b>. A stashed interview is redeemed exactly
/// once; a preview is re-read on every keystroke that survives the debounce.
/// </para>
///
/// <para>
/// In memory, and losing it costs nothing: the circuit writes the answers again on the next edit,
/// and a miss renders as "this preview has expired" rather than an error. The token is 16 random
/// bytes for the same reason the draft store's is — it is the only thing between one visitor's
/// half-finished answers and another visitor guessing a URL.
/// </para>
/// </summary>
public sealed class OnboardingPreviewStore(IMemoryCache cache)
{
    /// <summary>
    /// Sliding, because it should outlive a slow interview but not a browser left open overnight.
    /// Each edit renews it, so the only way to expire is to stop answering.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>A fresh token for one wizard session. Held by the circuit for its lifetime.</summary>
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>
    /// Overwrites the answers behind <paramref name="token"/>. Called on every debounced edit, so
    /// it must stay cheap: the profile is stored as-is and nothing is generated here.
    /// </summary>
    public void Put(string token, BusinessProfile answers) =>
        cache.Set(CacheKey(token), answers, new MemoryCacheEntryOptions { SlidingExpiration = Lifetime });

    /// <summary>The answers, or null once the entry has expired. Reading does not remove it.</summary>
    public BusinessProfile? Get(string? token) =>
        string.IsNullOrWhiteSpace(token) ? null
            : cache.TryGetValue(CacheKey(token), out BusinessProfile? answers) ? answers
            : null;

    /// <summary>Dropped when the wizard finishes, so a redeemed interview leaves nothing behind.</summary>
    public void Forget(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            cache.Remove(CacheKey(token));
        }
    }

    // A prefix distinct from the draft store's: the two hold different shapes under similar keys,
    // and a collision would hand a preview token a redeemable interview.
    private static string CacheKey(string token) => $"onboarding-preview:{token}";
}
