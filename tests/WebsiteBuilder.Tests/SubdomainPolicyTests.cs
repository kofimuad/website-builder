using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Tests;

public class SubdomainPolicyTests
{
    private static readonly TenantResolutionOptions Options = new() { PlatformDomain = "platform.com" };

    [Theory]
    [InlineData("joes-plumbing")]
    [InlineData("acme")]
    [InlineData("shop24")]
    [InlineData("a-b-c")]
    public void A_reasonable_address_is_accepted(string candidate)
    {
        Assert.Equal(SubdomainProblem.None, SubdomainPolicy.Validate(candidate, Options));
    }

    [Theory]
    [InlineData("", SubdomainProblem.Empty)]
    [InlineData("   ", SubdomainProblem.Empty)]
    [InlineData("ab", SubdomainProblem.TooShort)]
    [InlineData("joes plumbing", SubdomainProblem.InvalidCharacters)]
    [InlineData("joes_plumbing", SubdomainProblem.InvalidCharacters)]
    [InlineData("joes.plumbing", SubdomainProblem.InvalidCharacters)]
    [InlineData("café", SubdomainProblem.InvalidCharacters)]
    [InlineData("-acme", SubdomainProblem.EdgeHyphen)]
    [InlineData("acme-", SubdomainProblem.EdgeHyphen)]
    [InlineData("joes--plumbing", SubdomainProblem.DoubleHyphen)]
    [InlineData("xn--fiqs8s", SubdomainProblem.DoubleHyphen)]
    [InlineData("admin", SubdomainProblem.Reserved)]
    [InlineData("send", SubdomainProblem.Reserved)]
    [InlineData("secure", SubdomainProblem.Reserved)]
    public void A_bad_address_is_rejected_with_the_reason(string candidate, SubdomainProblem expected)
    {
        Assert.Equal(expected, SubdomainPolicy.Validate(candidate, Options));
    }

    [Fact]
    public void Too_long_is_rejected()
    {
        var candidate = new string('a', SubdomainPolicy.MaxLength + 1);

        Assert.Equal(SubdomainProblem.TooLong, SubdomainPolicy.Validate(candidate, Options));
    }

    [Theory]
    [InlineData("  ACME  ", "acme")]
    [InlineData("Joes-Plumbing", "joes-plumbing")]
    public void Input_is_normalised_before_anything_else(string typed, string expected)
    {
        // Classify lower-cases the incoming host, so an address stored with a capital would never
        // match a request for it.
        Assert.Equal(expected, SubdomainPolicy.Normalize(typed));
        Assert.Equal(SubdomainProblem.None, SubdomainPolicy.Validate(typed, Options));
    }

    [Fact]
    public void A_reserved_name_is_rejected_case_insensitively()
    {
        Assert.Equal(SubdomainProblem.Reserved, SubdomainPolicy.Validate("ADMIN", Options));
    }

    [Fact]
    public void Every_problem_has_a_message_that_does_not_say_subdomain()
    {
        foreach (var problem in Enum.GetValues<SubdomainProblem>())
        {
            var message = SubdomainPolicy.Describe(problem);

            if (problem == SubdomainProblem.None)
            {
                Assert.Equal("", message);
                continue;
            }

            Assert.NotEqual("", message);
            Assert.DoesNotContain("subdomain", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DNS", message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Anything_the_suggester_produces_passes_the_policy()
    {
        // The two must agree: an address auto-assigned at onboarding has to survive the validation
        // the owner's own typing is put through, or first publish would reject their own site.
        string[] names =
        [
            "Joe's Plumbing", "Café Ámà", "  ", "A", "!!!",
            "The Very Long Name Of A Business That Goes On And On And On For Ever",
        ];

        foreach (var name in names)
        {
            var slug = SubdomainSuggester.Slugify(name);

            Assert.Equal(SubdomainProblem.None, SubdomainPolicy.Validate(slug, Options));
        }
    }
}
