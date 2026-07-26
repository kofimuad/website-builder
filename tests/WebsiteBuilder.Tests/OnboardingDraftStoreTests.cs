using Microsoft.Extensions.Caching.Memory;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Web.Onboarding;

namespace WebsiteBuilder.Tests;

/// <summary>
/// The interview is answered anonymously and built after sign-in (WB-15), so the answers have to
/// survive a redirect out to Google or an email client and back.
/// </summary>
public class OnboardingDraftStoreTests
{
    private static OnboardingDraftStore Create() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    private static BusinessProfile Answers(string name = "Joe's Plumbing") => new()
    {
        BusinessName = name,
        Category = "plumber",
        Offerings = ["Drain clearing", "Leak detection"],
        PhoneNumber = "+233200000000",
        AddressLines = ["12 High Street", "Osu"],
    };

    [Fact]
    public void Stashed_answers_come_back_intact()
    {
        var store = Create();
        var key = store.Stash(Answers());

        var taken = store.Take(key);

        Assert.NotNull(taken);
        Assert.Equal("Joe's Plumbing", taken!.BusinessName);
        Assert.Equal(["Drain clearing", "Leak detection"], taken.Offerings);
        Assert.Equal(["12 High Street", "Osu"], taken.AddressLines);
    }

    [Fact]
    public void A_stash_can_only_be_claimed_once()
    {
        var store = Create();
        var key = store.Stash(Answers());

        Assert.NotNull(store.Take(key));

        // The key travels in a return URL, which lands in browser history — one use only.
        Assert.Null(store.Take(key));
    }

    [Fact]
    public void An_unknown_key_yields_nothing()
    {
        var store = Create();

        Assert.Null(store.Take("deadbeef"));
        Assert.Null(store.Take(""));
        Assert.Null(store.Take(null));
    }

    [Fact]
    public void Separate_interviews_do_not_collide()
    {
        var store = Create();

        var first = store.Stash(Answers("First Co"));
        var second = store.Stash(Answers("Second Co"));

        Assert.NotEqual(first, second);
        Assert.Equal("Second Co", store.Take(second)!.BusinessName);
        Assert.Equal("First Co", store.Take(first)!.BusinessName);
    }

    [Fact]
    public void The_key_is_long_enough_to_not_be_guessable()
    {
        var store = Create();

        // 16 random bytes, hex encoded. Anyone holding the key can claim the answers as their site.
        Assert.Equal(32, store.Stash(Answers()).Length);
    }
}

public class OnboardingWizardRestoreTests
{
    [Fact]
    public void Restoring_answers_lands_on_a_finished_interview()
    {
        var wizard = new OnboardingWizard();

        wizard.Restore(new BusinessProfile
        {
            BusinessName = "Restored Co",
            Category = "baker",
            Offerings = ["Cakes"],
            PhoneNumber = "+233200000000",
        });

        // They signed in mid-flow; sending them back to question one would be a betrayal.
        Assert.True(wizard.IsComplete);
        Assert.Equal("Restored Co", wizard.Answers.BusinessName);
        Assert.Equal(OnboardingStep.ServiceArea, wizard.CurrentStep);
    }

    [Fact]
    public void Restoring_nothing_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new OnboardingWizard().Restore(null!));
    }
}
