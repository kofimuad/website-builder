using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using WebsiteBuilder.Core.Generation;

namespace WebsiteBuilder.Web.Generation;

/// <summary>
/// The real model call behind <see cref="IModelJsonCompletion"/>, against Google's Gemini API.
/// <para>
/// This talks to <c>generateContent</c> over plain HTTP rather than through a client library. The
/// call is one request with one JSON response, the wire format is stable and documented, and a
/// dependency whose release cadence we do not control is a poor trade for the twenty lines it
/// would save. It also means the whole thing can be tested against a stub handler.
/// </para>
/// <para>
/// Structured output is requested with <c>responseSchema</c>, which takes the same JSON Schema the
/// rest of the codebase uses, minus a handful of keywords the OpenAPI 3.0 subset does not define —
/// see <c>ToNode</c>.
/// </para>
/// </summary>
public sealed class GeminiJsonCompletion(HttpClient http, IOptions<GeminiOptions> options)
    : IModelJsonCompletion
{
    private readonly GeminiOptions _options = options.Value;

    public async Task<ModelCompletionResult> CompleteAsync(
        string system,
        string user,
        IReadOnlyDictionary<string, JsonElement> schema,
        CancellationToken cancellationToken = default)
    {
        var request = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = system }),
            },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = user }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = ToNode(schema),
                ["maxOutputTokens"] = _options.MaxOutputTokens,
            },
        };

        using var response = await http.PostAsJsonAsync(
            $"models/{_options.Model}:generateContent", request, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Google's error body says exactly which part of the request it disliked — an
            // unsupported schema keyword, a retired model id, a rejected key. Losing it here would
            // leave nothing behind but a generic site and a puzzled owner.
            throw new HttpRequestException(
                $"Gemini returned {(int)response.StatusCode} for model '{_options.Model}': {Trim(body)}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var text = ExtractText(root, out var finishReason);

        if (string.IsNullOrWhiteSpace(text))
        {
            // A truncated or filtered response is not an empty answer, and retrying the same
            // prompt will not fix it. Say which it was.
            throw new InvalidOperationException(
                $"Gemini returned no usable text (finishReason: {finishReason ?? "none"}). " +
                (finishReason == "MAX_TOKENS"
                    ? $"Raise Gemini:MaxOutputTokens above {_options.MaxOutputTokens}."
                    : $"Response: {Trim(body)}"));
        }

        var (inputTokens, outputTokens) = ReadUsage(root);

        return new ModelCompletionResult(text, inputTokens, outputTokens, EstimateCost(inputTokens, outputTokens));
    }

    /// <summary>Concatenates the text parts of the first candidate, as the response may be split across several.</summary>
    private static string ExtractText(JsonElement root, out string? finishReason)
    {
        finishReason = null;

        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            return "";
        }

        var candidate = candidates[0];

        if (candidate.TryGetProperty("finishReason", out var reason) && reason.ValueKind == JsonValueKind.String)
        {
            finishReason = reason.GetString();
        }

        if (!candidate.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        var text = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
            {
                text.Append(value.GetString());
            }
        }

        return text.ToString();
    }

    /// <summary>Usage is advisory: a missing count must not cost us a perfectly good site.</summary>
    private static (long Input, long Output) ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage))
        {
            return (0, 0);
        }

        return (Count(usage, "promptTokenCount"), Count(usage, "candidatesTokenCount"));

        static long Count(JsonElement usage, string name) =>
            usage.TryGetProperty(name, out var value) && value.TryGetInt64(out var count) ? count : 0;
    }

    private decimal EstimateCost(long inputTokens, long outputTokens) =>
        inputTokens / 1_000_000m * _options.InputPricePerMillion
        + outputTokens / 1_000_000m * _options.OutputPricePerMillion;

    /// <summary>
    /// Keywords that are valid JSON Schema but are not in the OpenAPI 3.0 subset Gemini accepts
    /// for <c>responseSchema</c>. Sending one is a 400, and a 400 here is invisible: generation
    /// falls back to the template and the site simply reads generically.
    /// </summary>
    private static readonly HashSet<string> Unsupported =
        new(StringComparer.Ordinal) { "additionalProperties", "$schema", "$id", "$defs", "definitions" };

    /// <summary>
    /// Copies the schema, dropping the keywords above at every level.
    /// <para>
    /// This is a translation layer, which is the thing this class set out not to have. It earns its
    /// place: the list is five names rather than a dialect, dropping them cannot change what the
    /// schema permits in any way that matters here (the properties and their types are what
    /// constrain the model), and the alternative is a provider that answers 400 to every request
    /// while the product looks like it is merely uninspired.
    /// </para>
    /// </summary>
    private static JsonNode ToNode(IReadOnlyDictionary<string, JsonElement> schema)
    {
        var node = new JsonObject();

        foreach (var (key, value) in schema)
        {
            if (Unsupported.Contains(key))
            {
                continue;
            }

            node[key] = Clean(JsonSerializer.SerializeToNode(value));
        }

        return node;
    }

    private static JsonNode? Clean(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                var copy = new JsonObject();
                foreach (var (key, value) in o)
                {
                    if (!Unsupported.Contains(key))
                    {
                        copy[key] = Clean(value);
                    }
                }

                return copy;

            case JsonArray a:
                return new JsonArray(a.Select(Clean).ToArray());

            default:
                return node?.DeepClone();
        }
    }

    /// <summary>Error bodies can carry the whole echoed request; keep the log readable.</summary>
    private static string Trim(string body) =>
        body.Length <= 800 ? body : string.Concat(body.AsSpan(0, 800), "…");
}
