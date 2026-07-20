using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WebsiteBuilder.Core.SiteModel;

/// <summary>
/// The single place site definitions cross the JSON boundary. Reading upgrades older documents
/// to the current schema first, so the rest of the app only ever sees the current shape.
/// </summary>
public static class SiteDefinitionSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(), new SiteSectionJsonConverter() },
    };

    /// <summary>
    /// Upgrade steps keyed by the version they upgrade *from*. Each step rewrites the JSON in
    /// place and must leave a document valid at key + 1. Never edit a published step; add a new one.
    /// </summary>
    private static readonly Dictionary<int, Action<JsonObject>> Upgrades = new()
    {
        // v0 is any document written before schemaVersion existed.
        [0] = _ => { },
    };

    public static string Serialize(SiteDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        definition.SchemaVersion = SiteDefinition.CurrentSchemaVersion;
        return JsonSerializer.Serialize(definition, Options);
    }

    public static SiteDefinition Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var node = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidSiteDefinitionException("Site definition JSON is not an object.");

        var version = node["schemaVersion"]?.GetValue<int>() ?? 0;

        if (version > SiteDefinition.CurrentSchemaVersion)
        {
            // An older deployment must not silently drop fields it does not understand.
            throw new InvalidSiteDefinitionException(
                $"Site definition is schema version {version}, but this build understands at most " +
                $"{SiteDefinition.CurrentSchemaVersion}.");
        }

        while (version < SiteDefinition.CurrentSchemaVersion)
        {
            if (!Upgrades.TryGetValue(version, out var upgrade))
            {
                throw new InvalidSiteDefinitionException(
                    $"No upgrade step from site definition schema version {version}.");
            }

            upgrade(node);
            version++;
            node["schemaVersion"] = version;
        }

        return node.Deserialize<SiteDefinition>(Options)
            ?? throw new InvalidSiteDefinitionException("Site definition JSON deserialized to null.");
    }
}

public sealed class InvalidSiteDefinitionException(string message) : Exception(message);
