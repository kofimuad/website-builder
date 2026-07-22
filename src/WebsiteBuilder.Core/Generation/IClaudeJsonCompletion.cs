using System.Text.Json;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// A single structured-output call to Claude, abstracted so the generation logic (retries, guard,
/// assembly) can be tested without the SDK or a network. The Anthropic-backed implementation lives
/// in the web project.
/// </summary>
public interface IClaudeJsonCompletion
{
    Task<ClaudeCompletionResult> CompleteAsync(
        string system,
        string user,
        IReadOnlyDictionary<string, JsonElement> schema,
        CancellationToken cancellationToken = default);
}

/// <summary>The model's JSON output plus the token usage for cost accounting.</summary>
public sealed record ClaudeCompletionResult(string Json, long InputTokens, long OutputTokens);
