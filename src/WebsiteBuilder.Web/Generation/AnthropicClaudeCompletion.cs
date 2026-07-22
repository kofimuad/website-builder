using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using WebsiteBuilder.Core.Generation;

namespace WebsiteBuilder.Web.Generation;

/// <summary>
/// The real Claude call behind <see cref="IClaudeJsonCompletion"/>. Uses structured outputs so the
/// response is constrained to the site-copy schema, and reports token usage for cost accounting.
/// </summary>
public sealed class AnthropicClaudeCompletion(AnthropicClient client) : IClaudeJsonCompletion
{
    // Small, self-contained copy — comfortably under the non-streaming size where HTTP timeouts bite.
    private const int MaxTokens = 8000;

    public async Task<ClaudeCompletionResult> CompleteAsync(
        string system,
        string user,
        IReadOnlyDictionary<string, JsonElement> schema,
        CancellationToken cancellationToken = default)
    {
        var response = await client.Messages.Create(
            new MessageCreateParams
            {
                Model = Model.ClaudeOpus4_8,
                MaxTokens = MaxTokens,
                System = system,
                Messages = [new() { Role = Role.User, Content = user }],
                OutputConfig = new OutputConfig
                {
                    Format = new JsonOutputFormat
                    {
                        Schema = new Dictionary<string, JsonElement>(schema),
                    },
                },
            },
            cancellationToken);

        var text = new StringBuilder();
        foreach (var block in response.Content.Select(b => b.Value).OfType<TextBlock>())
        {
            text.Append(block.Text);
        }

        return new ClaudeCompletionResult(text.ToString(), response.Usage.InputTokens, response.Usage.OutputTokens);
    }
}
