using System.Text.Json;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// The JSON schema the model's output is constrained to. Shared by the prompt (for documentation)
/// and the completion call (as the structured-output format), so the two can never drift.
/// </summary>
public static class SiteGenerationSchema
{
    private const string SchemaJson =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "heroHeadline": { "type": "string" },
            "heroSubheadline": { "type": "string" },
            "aboutHeading": { "type": "string" },
            "aboutBody": { "type": "string" },
            "services": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "title": { "type": "string" },
                  "description": { "type": "string" }
                },
                "required": ["title", "description"]
              }
            },
            "ctaHeadline": { "type": "string" },
            "ctaButtonLabel": { "type": "string" },
            "seoTitle": { "type": "string" },
            "seoDescription": { "type": "string" },
            "tagline": { "type": "string" },
            "palette": { "type": "string", "enum": ["friendly", "professional", "bold"] }
          },
          "required": [
            "heroHeadline", "heroSubheadline", "aboutHeading", "aboutBody", "services",
            "ctaHeadline", "ctaButtonLabel", "seoTitle", "seoDescription", "tagline", "palette"
          ]
        }
        """;

    public static Dictionary<string, JsonElement> Build() =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(SchemaJson)
        ?? throw new InvalidOperationException("Site generation schema failed to parse.");
}
