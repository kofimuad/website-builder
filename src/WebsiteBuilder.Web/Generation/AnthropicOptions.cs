namespace WebsiteBuilder.Web.Generation;

/// <summary>Everything about the Anthropic provider that might need to change without a deploy.</summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>
    /// From console.anthropic.com, and it starts <c>sk-ant-</c>. Read from <c>ANTHROPIC_API_KEY</c>
    /// as well, because that is the name every Anthropic example and SDK uses by default.
    /// <para>
    /// This is <em>API</em> credit. A Claude subscription — Pro, Max, or Claude Code — funds
    /// nothing here and issues no key of this shape; the two balances are separate. Getting this
    /// wrong looks like a 401 in the deploy log and a generic-sounding website in the product.
    /// </para>
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Configurable because model ids move faster than deploys, and because dropping to a cheaper
    /// model is the first thing anyone will want when a bill surprises them.
    /// </summary>
    public string Model { get; set; } = "claude-opus-5";

    /// <summary>
    /// Caps thinking <em>and</em> the answer together. Opus 5 thinks by default, so a budget sized
    /// for the copy alone truncates the JSON mid-object and the whole generation fails to parse —
    /// hence a ceiling several times larger than the copy needs.
    /// </summary>
    public int MaxTokens { get; set; } = 16000;

    /// <summary>
    /// How hard the model works before answering: <c>low</c>, <c>medium</c>, <c>high</c>,
    /// <c>xhigh</c> or <c>max</c>.
    /// <para>
    /// Low by default. Writing a page of marketing copy from a filled-in form is not a reasoning
    /// problem, and this call happens in the foreground of onboarding with a person watching a
    /// spinner — effort is the lever that decides how long they watch it, and how much the site
    /// costs to make. Raise it if the copy reads thin.
    /// </para>
    /// </summary>
    public string Effort { get; set; } = "low";

    /// <summary>List price, US dollars per million tokens, for Claude Opus 5.</summary>
    public decimal InputPricePerMillion { get; set; } = 5.00m;

    public decimal OutputPricePerMillion { get; set; } = 25.00m;

    /// <summary>
    /// The SDK's own default is ten minutes, which is not a timeout so much as a hang. Nobody
    /// waits that long for a website; failing to the template is the better outcome.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 90;
}
