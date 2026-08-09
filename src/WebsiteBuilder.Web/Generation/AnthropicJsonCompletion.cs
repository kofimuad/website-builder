using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;
using WebsiteBuilder.Core.Generation;

namespace WebsiteBuilder.Web.Generation;

/// <summary>
/// The real model call behind <see cref="IModelJsonCompletion"/>, against Anthropic's Messages API
/// through the official SDK.
/// <para>
/// Unlike the Gemini implementation, this one goes through a client library rather than raw HTTP:
/// the SDK is Anthropic's own, it models structured outputs as a first-class parameter, and the
/// schema needs no translation — <see cref="SiteGenerationSchema"/> already carries the
/// <c>additionalProperties: false</c> and <c>required</c> that Anthropic requires and Gemini
/// rejects.
/// </para>
/// <para>
/// Every failure here is thrown rather than swallowed, because the caller is
/// <c>FallbackSiteGenerator</c>: it turns an exception into a template site, which is a working
/// website, and writes the message to the log, which is the only place anyone will find out why
/// the copy reads generically.
/// </para>
/// </summary>
public sealed class AnthropicJsonCompletion(AnthropicClient client, IOptions<AnthropicOptions> options)
    : IModelJsonCompletion
{
    private readonly AnthropicOptions _options = options.Value;

    public async Task<ModelCompletionResult> CompleteAsync(
        string system,
        string user,
        IReadOnlyDictionary<string, JsonElement> schema,
        CancellationToken cancellationToken = default)
    {
        var response = await client.Messages.Create(
            new MessageCreateParams
            {
                Model = _options.Model,
                MaxTokens = _options.MaxTokens,
                System = system,
                Messages = [new() { Role = Role.User, Content = user }],
                OutputConfig = new OutputConfig
                {
                    Effort = _options.Effort,
                    Format = new JsonOutputFormat
                    {
                        Schema = new Dictionary<string, JsonElement>(schema),
                    },
                },
            },
            cancellationToken);

        // Safety classifiers can decline a request and still answer 200, with empty content. Read
        // the stop reason before the content or the symptom is an inscrutable "no text" error.
        var stopReason = response.StopReason?.ToString();

        if (Is(stopReason, "refusal"))
        {
            throw new InvalidOperationException(
                $"Claude declined the request (category: {response.StopDetails?.Category?.ToString() ?? "none"}). " +
                $"{response.StopDetails?.Explanation}".TrimEnd());
        }

        var text = new StringBuilder();
        foreach (var block in response.Content.Select(b => b.Value).OfType<TextBlock>())
        {
            text.Append(block.Text);
        }

        if (Is(stopReason, "max_tokens"))
        {
            // The JSON is cut off mid-object, so it will not parse. Say so here rather than let it
            // surface three layers up as a deserialisation error about a missing brace.
            throw new InvalidOperationException(
                $"Claude hit the {_options.MaxTokens}-token ceiling before finishing the JSON. " +
                "Raise Anthropic:MaxTokens, or lower Anthropic:Effort so less of the budget goes on thinking.");
        }

        if (string.IsNullOrWhiteSpace(text.ToString()))
        {
            throw new InvalidOperationException(
                $"Claude returned no text (stop reason: {stopReason ?? "none"}).");
        }

        var input = response.Usage.InputTokens;
        var output = response.Usage.OutputTokens;

        return new ModelCompletionResult(text.ToString(), input, output, EstimateCost(input, output));
    }

    /// <summary>
    /// Compares a stop reason to a wire value without assuming how the SDK's enum renders itself.
    /// The wire form is <c>max_tokens</c>; a generated enum may well say <c>MaxTokens</c>. Getting
    /// this wrong costs a clear error message, so it is not worth being precious about.
    /// </summary>
    private static bool Is(string? stopReason, string wireValue) =>
        stopReason is not null
        && string.Equals(
            stopReason.Replace("_", "", StringComparison.Ordinal),
            wireValue.Replace("_", "", StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase);

    private decimal EstimateCost(long inputTokens, long outputTokens) =>
        inputTokens / 1_000_000m * _options.InputPricePerMillion
        + outputTokens / 1_000_000m * _options.OutputPricePerMillion;
}
