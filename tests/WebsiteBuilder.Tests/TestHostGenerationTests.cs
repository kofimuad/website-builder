using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebsiteBuilder.Core.Generation;
using WebsiteBuilder.Web.Generation;

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

/// <summary>
/// Boots the real host with a chosen set of provider keys, to check which provider the wiring
/// selects. The keys are nonsense and no request is ever made: constructing a client does not call
/// anything, and resolving a service is not the same as using it.
/// </summary>
public sealed class ProviderAppFactory(PostgresFixture fixture, Dictionary<string, string?> keys)
    : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(
            new Dictionary<string, string?>(keys)
            {
                ["ConnectionStrings:Default"] = fixture.ConnectionString,
                ["TenantResolution:PlatformDomain"] = "platform.com",
            }));

        return base.CreateHost(builder);
    }
}

/// <summary>
/// Which provider a deploy is actually talking to. Two keys can be present at once — one left over
/// on Railway from the last provider is the normal case — so the choice between them is a rule the
/// application makes, and a rule nobody has written down is a rule that quietly changes.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class ModelProviderSelectionTests(PostgresFixture fixture)
{
    private static ProviderAppFactory Host(PostgresFixture fixture, string? anthropic, string? gemini) =>
        new(fixture, new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = anthropic ?? "",
            ["ANTHROPIC_API_KEY"] = "",
            ["Gemini:ApiKey"] = gemini ?? "",
            ["GEMINI_API_KEY"] = "",
        });

    [Fact]
    public void An_anthropic_key_puts_claude_behind_the_generator()
    {
        using var host = Host(fixture, anthropic: "sk-ant-not-a-real-key", gemini: null);

        Assert.IsType<AnthropicJsonCompletion>(host.Services.GetRequiredService<IModelJsonCompletion>());
        Assert.IsType<FallbackSiteGenerator>(host.Services.GetRequiredService<ISiteGenerator>());
        Assert.NotNull(host.Services.GetService<ISectionAssistant>());
    }

    [Fact]
    public void A_gemini_key_alone_still_selects_gemini()
    {
        using var host = Host(fixture, anthropic: null, gemini: "not-a-real-key");

        Assert.IsType<GeminiJsonCompletion>(host.Services.GetRequiredService<IModelJsonCompletion>());
        Assert.IsType<FallbackSiteGenerator>(host.Services.GetRequiredService<ISiteGenerator>());
    }

    [Fact]
    public void With_both_keys_present_anthropic_wins()
    {
        // The stated rule. If this ever flips, it should flip in a diff someone reviewed rather
        // than because two registrations swapped order.
        using var host = Host(fixture, anthropic: "sk-ant-not-a-real-key", gemini: "not-a-real-key");

        Assert.IsType<AnthropicJsonCompletion>(host.Services.GetRequiredService<IModelJsonCompletion>());
    }

    [Fact]
    public void With_no_key_at_all_there_is_no_model_to_call()
    {
        using var host = Host(fixture, anthropic: null, gemini: null);

        Assert.Null(host.Services.GetService<IModelJsonCompletion>());
        Assert.IsType<TemplateSiteGenerator>(host.Services.GetRequiredService<ISiteGenerator>());
    }
}
