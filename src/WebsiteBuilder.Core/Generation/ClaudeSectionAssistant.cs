using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// Claude-backed section reviser. It sends only the selected section's text fields plus the
/// owner's instruction, and applies the returned revisions back onto a copy of that section —
/// so structure, ids, URLs, times, and every other section are untouched by construction.
/// </summary>
public sealed class ClaudeSectionAssistant(
    IClaudeJsonCompletion completion,
    ILogger<ClaudeSectionAssistant> logger) : ISectionAssistant
{
    private const decimal InputPricePerMillion = 5.00m;
    private const decimal OutputPricePerMillion = 25.00m;

    private const string System =
        """
        You are helping a small-business owner edit one section of their website. You are given the
        section's text fields (each with a path) and an instruction. Return revised text for the
        fields the instruction affects.

        Rules:
        - Only change wording. Keep every path exactly as given; do not invent new paths.
        - Return only the fields you actually changed.
        - Keep the owner's meaning. Apply facts the instruction gives you, but do not invent facts
          it doesn't (no prices, phone numbers, addresses, dates, or claims that weren't provided).
        - Warm, plain, concrete copy. No clichés.
        """;

    private static readonly Dictionary<string, JsonElement> Schema =
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "revisions": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "properties": { "path": { "type": "string" }, "text": { "type": "string" } },
                    "required": ["path", "text"]
                  }
                }
              },
              "required": ["revisions"]
            }
            """)!;

    public async Task<SiteSection> ReviseAsync(SiteSection section, string instruction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);

        var originalType = section.GetType();
        var originalId = section.Id;

        var node = JsonNode.Parse(JsonSerializer.Serialize(section, SiteDefinitionSerializer.Options)) as JsonObject
            ?? throw new SectionAssistantException("The section could not be read as JSON.");

        var fields = SectionText.Collect(node);
        var allowedPaths = fields.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);

        var result = await completion.CompleteAsync(System, BuildUser(fields, instruction), Schema, cancellationToken);
        LogCost(result);

        var revisions = ParseRevisions(result.Json)
            // Ignore any path the model invented — only the section's real fields may change.
            .Where(r => allowedPaths.Contains(r.Path))
            .GroupBy(r => r.Path)
            .ToDictionary(g => g.Key, g => g.Last().Text, StringComparer.Ordinal);

        SectionText.Apply(node, revisions);

        var revised = node.Deserialize<SiteSection>(SiteDefinitionSerializer.Options)
            ?? throw new SectionAssistantException("The revised section could not be read back.");

        if (revised.GetType() != originalType)
        {
            throw new SectionAssistantException("The revision changed the section type.");
        }

        revised.Id = originalId;
        return revised;
    }

    private static string BuildUser(IReadOnlyList<SectionTextField> fields, string instruction)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Instruction:");
        builder.AppendLine(instruction.Trim());
        builder.AppendLine();
        builder.AppendLine("Current text fields:");
        builder.AppendLine(JsonSerializer.Serialize(fields.Select(f => new { f.Path, f.Text })));
        return builder.ToString();
    }

    private static IEnumerable<SectionTextField> ParseRevisions(string json)
    {
        RevisionEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<RevisionEnvelope>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw new SectionAssistantException("The assistant returned an unreadable response.", ex);
        }

        return envelope?.Revisions ?? [];
    }

    private void LogCost(ClaudeCompletionResult result)
    {
        var cost = result.InputTokens / 1_000_000m * InputPricePerMillion
                   + result.OutputTokens / 1_000_000m * OutputPricePerMillion;
        logger.LogInformation(
            "Section assistant request: {InputTokens} in + {OutputTokens} out tokens, ~${Cost} (USD).",
            result.InputTokens, result.OutputTokens, cost.ToString("0.0000"));
    }

    private sealed record RevisionEnvelope(List<SectionTextField> Revisions);
}
