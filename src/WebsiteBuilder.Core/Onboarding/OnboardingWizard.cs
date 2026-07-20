namespace WebsiteBuilder.Core.Onboarding;

public enum OnboardingStep
{
    BusinessName,
    Category,
    Offerings,
    Tone,
    Photos,
    Contact,
    ServiceArea,
}

/// <summary>
/// Drives the interview: which question is on screen, whether it can be left yet, and what the
/// answers add up to. Kept free of UI so the flow can be tested without rendering anything.
/// </summary>
public sealed class OnboardingWizard
{
    public static readonly IReadOnlyList<OnboardingStep> Steps = Enum.GetValues<OnboardingStep>();

    /// <summary>Answers accumulate in one object, so stepping back never discards them.</summary>
    public BusinessProfile Answers { get; } = new();

    public int CurrentIndex { get; private set; }

    public OnboardingStep CurrentStep => Steps[CurrentIndex];

    public bool IsFirstStep => CurrentIndex == 0;

    public bool IsComplete { get; private set; }

    /// <summary>1-based, for "Question 3 of 7".</summary>
    public int StepNumber => CurrentIndex + 1;

    public int StepCount => Steps.Count;

    /// <summary>Optional questions can be left empty; the interview must never trap someone.</summary>
    public static bool IsOptional(OnboardingStep step) =>
        step is OnboardingStep.Photos or OnboardingStep.ServiceArea;

    /// <summary>Returns a plain-language problem with the current answer, or null if it is fine.</summary>
    public string? Validate()
    {
        switch (CurrentStep)
        {
            case OnboardingStep.BusinessName when string.IsNullOrWhiteSpace(Answers.BusinessName):
                return "Please tell us what your business is called.";

            case OnboardingStep.Category when string.IsNullOrWhiteSpace(Answers.Category):
                return "Please say what kind of business this is.";

            case OnboardingStep.Offerings when Answers.Offerings.All(string.IsNullOrWhiteSpace):
                return "Please list at least one thing you offer.";

            case OnboardingStep.Contact when
                string.IsNullOrWhiteSpace(Answers.PhoneNumber)
                && string.IsNullOrWhiteSpace(Answers.WhatsAppNumber)
                && string.IsNullOrWhiteSpace(Answers.Email):
                return "Please give customers at least one way to reach you.";

            default:
                return null;
        }
    }

    /// <summary>Moves on if the answer is usable. Returns the problem to show otherwise.</summary>
    public string? TryAdvance()
    {
        var problem = Validate();
        if (problem is not null)
        {
            return problem;
        }

        Tidy();

        if (CurrentIndex == Steps.Count - 1)
        {
            IsComplete = true;
        }
        else
        {
            CurrentIndex++;
        }

        return null;
    }

    public void GoBack()
    {
        if (IsFirstStep)
        {
            return;
        }

        IsComplete = false;
        CurrentIndex--;
    }

    /// <summary>Drops blank lines the owner left behind while typing.</summary>
    private void Tidy()
    {
        if (CurrentStep == OnboardingStep.Offerings)
        {
            Answers.Offerings = Answers.Offerings
                .Select(o => o.Trim())
                .Where(o => o.Length > 0)
                .ToList();
        }

        if (CurrentStep == OnboardingStep.ServiceArea)
        {
            Answers.AddressLines = Answers.AddressLines
                .Select(a => a.Trim())
                .Where(a => a.Length > 0)
                .ToList();
        }
    }
}
