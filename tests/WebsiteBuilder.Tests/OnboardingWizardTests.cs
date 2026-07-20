using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Tests;

public class OnboardingWizardTests
{
    private static OnboardingWizard AtStep(OnboardingStep step)
    {
        var wizard = new OnboardingWizard();
        wizard.Answers.BusinessName = "Joe's Plumbing";
        wizard.Answers.Category = "plumber";
        wizard.Answers.Offerings = ["Drain clearing"];
        wizard.Answers.PhoneNumber = "+233200000000";

        while (wizard.CurrentStep != step)
        {
            Assert.Null(wizard.TryAdvance());
        }

        return wizard;
    }

    [Fact]
    public void The_interview_is_seven_questions_starting_with_the_business_name()
    {
        var wizard = new OnboardingWizard();

        Assert.Equal(7, wizard.StepCount);
        Assert.Equal(OnboardingStep.BusinessName, wizard.CurrentStep);
        Assert.Equal(1, wizard.StepNumber);
        Assert.True(wizard.IsFirstStep);
    }

    [Fact]
    public void A_required_question_cannot_be_skipped()
    {
        var wizard = new OnboardingWizard();

        var problem = wizard.TryAdvance();

        Assert.NotNull(problem);
        Assert.Equal(OnboardingStep.BusinessName, wizard.CurrentStep);
    }

    [Fact]
    public void Problems_are_phrased_without_jargon()
    {
        var wizard = new OnboardingWizard();

        var problem = wizard.TryAdvance()!;

        Assert.StartsWith("Please", problem);
        foreach (var jargon in new[] { "field", "required", "invalid", "null", "validation" })
        {
            Assert.DoesNotContain(jargon, problem, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Optional_questions_can_be_left_empty()
    {
        var wizard = AtStep(OnboardingStep.Photos);

        Assert.True(OnboardingWizard.IsOptional(OnboardingStep.Photos));
        Assert.Null(wizard.TryAdvance());
        Assert.Equal(OnboardingStep.Contact, wizard.CurrentStep);
    }

    [Fact]
    public void Going_back_keeps_the_answers_already_given()
    {
        var wizard = AtStep(OnboardingStep.Offerings);

        wizard.GoBack();
        wizard.GoBack();

        Assert.Equal(OnboardingStep.BusinessName, wizard.CurrentStep);
        Assert.Equal("Joe's Plumbing", wizard.Answers.BusinessName);
        Assert.Equal("plumber", wizard.Answers.Category);
    }

    [Fact]
    public void Back_does_nothing_on_the_first_question()
    {
        var wizard = new OnboardingWizard();

        wizard.GoBack();

        Assert.Equal(OnboardingStep.BusinessName, wizard.CurrentStep);
    }

    [Fact]
    public void At_least_one_way_to_get_in_touch_is_required()
    {
        var wizard = AtStep(OnboardingStep.Contact);
        wizard.Answers.PhoneNumber = null;

        Assert.NotNull(wizard.TryAdvance());

        wizard.Answers.Email = "joe@example.com";
        Assert.Null(wizard.TryAdvance());
    }

    [Fact]
    public void Blank_lines_in_list_answers_are_dropped()
    {
        var wizard = AtStep(OnboardingStep.Offerings);
        wizard.Answers.Offerings = ["Drain clearing", "   ", "", "Leak repair"];

        wizard.TryAdvance();

        Assert.Equal(["Drain clearing", "Leak repair"], wizard.Answers.Offerings);
    }

    [Fact]
    public void A_list_answer_of_only_blank_lines_does_not_count_as_answered()
    {
        var wizard = AtStep(OnboardingStep.Offerings);
        wizard.Answers.Offerings = ["  ", ""];

        Assert.NotNull(wizard.TryAdvance());
    }

    [Fact]
    public void The_interview_completes_after_the_last_question()
    {
        var wizard = AtStep(OnboardingStep.ServiceArea);

        Assert.False(wizard.IsComplete);
        Assert.Null(wizard.TryAdvance());
        Assert.True(wizard.IsComplete);
    }

    [Fact]
    public void Going_back_from_the_end_reopens_the_interview()
    {
        var wizard = AtStep(OnboardingStep.ServiceArea);
        wizard.TryAdvance();

        wizard.GoBack();

        Assert.False(wizard.IsComplete);
        Assert.Equal(OnboardingStep.Contact, wizard.CurrentStep);
    }
}

public class SubdomainSuggesterTests
{
    private static readonly TenantResolutionOptions Options = new();

    [Theory]
    [InlineData("Joe's Plumbing", "joes-plumbing")]
    [InlineData("  Spaced   Out  ", "spaced-out")]
    [InlineData("Café Ámà", "cafe-ama")]
    [InlineData("A&B Hair + Beauty", "a-b-hair-beauty")]
    [InlineData("--weird--", "weird")]
    public void A_business_name_becomes_a_usable_address(string name, string expected)
    {
        Assert.Equal(expected, SubdomainSuggester.Slugify(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("!!!")]
    [InlineData("東京")]
    public void A_name_with_nothing_usable_falls_back(string? name)
    {
        // A name that slugs to nothing must not produce an empty host.
        Assert.Equal("my-site", SubdomainSuggester.Slugify(name));
    }

    [Fact]
    public void Long_names_are_shortened_without_a_trailing_hyphen()
    {
        var slug = SubdomainSuggester.Slugify(new string('a', 30) + " " + new string('b', 30));

        Assert.True(slug.Length <= 40);
        Assert.DoesNotContain("--", slug);
        Assert.False(slug.EndsWith('-'));
    }

    [Fact]
    public async Task A_taken_address_gets_a_number_appended()
    {
        var taken = new HashSet<string> { "joes-plumbing", "joes-plumbing-2" };

        var suggestion = await SubdomainSuggester.SuggestAsync(
            "Joe's Plumbing", Options, (candidate, _) => Task.FromResult(taken.Contains(candidate)));

        Assert.Equal("joes-plumbing-3", suggestion);
    }

    [Fact]
    public async Task Reserved_addresses_are_never_suggested()
    {
        var suggestion = await SubdomainSuggester.SuggestAsync(
            "admin", Options, (_, _) => Task.FromResult(false));

        Assert.NotEqual("admin", suggestion);
        Assert.Equal("admin-2", suggestion);
    }
}
