namespace WebsiteBuilder.Core.Onboarding;

/// <summary>
/// The real stages a site passes through while it is being built, in order. Reported at genuine
/// pipeline boundaries so the progress screen advances with the work, never on a timer.
/// </summary>
public enum OnboardingProgress
{
    /// <summary>Reserving the address and saving the answers.</summary>
    Preparing,

    /// <summary>The model (or template) is writing the copy.</summary>
    WritingCopy,

    /// <summary>A draft was rejected (bad output or an invented fact) and is being rewritten.</summary>
    Revising,

    /// <summary>The copy is in; the pages are being assembled.</summary>
    BuildingPages,

    /// <summary>Saving the finished draft.</summary>
    Finishing,
}
