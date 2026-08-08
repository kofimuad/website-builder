using Microsoft.Extensions.DependencyInjection;
using WebsiteBuilder.Core.Generation;

namespace WebsiteBuilder.Tests;

/// <summary>
/// Pins the guarantee that the integration host never reaches the live model. Without it, every
/// test that completes onboarding is a billed API request, and the bill only shows up a month
/// later — the failure mode is a cost, not a red test, so it needs a test of its own.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class TestHostGenerationTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void Sites_are_generated_from_the_template_rather_than_the_live_model()
    {
        var generator = _factory.Services.GetRequiredService<ISiteGenerator>();

        Assert.IsType<TemplateSiteGenerator>(generator);
    }

    [Fact]
    public void The_section_assistant_is_absent_because_it_has_no_model_to_call()
    {
        Assert.Null(_factory.Services.GetService<ISectionAssistant>());
    }
}
