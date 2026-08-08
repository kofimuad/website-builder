using System.Text.Json;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// A single structured-output call to a language model, abstracted so the generation logic
/// (retries, guard, assembly) can be tested without an SDK or a network. The implementation lives
/// in the web project, which is also the only place that knows which provider is in use.
/// </summary>
public interface IModelJsonCompletion
{
    Task<ModelCompletionResult> CompleteAsync(
        string system,
        string user,
        IReadOnlyDictionary<string, JsonElement> schema,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The model's JSON output plus what the call cost.
/// <para>
/// The price is worked out by the implementation rather than by the caller: token prices are a
/// property of the provider and its model, and a generator that hardcoded them would quietly
/// report a different provider's prices the day one is swapped for another.
/// </para>
/// </summary>
public sealed record ModelCompletionResult(
    string Json,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCostUsd);
