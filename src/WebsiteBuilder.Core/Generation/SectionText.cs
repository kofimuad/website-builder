using System.Text.Json.Nodes;

namespace WebsiteBuilder.Core.Generation;

/// <summary>One editable text field of a section, addressed by a dotted/indexed path into its JSON.</summary>
public sealed record SectionTextField(string Path, string Text);

/// <summary>
/// Reads and writes the editable text of a section generically, by walking its JSON. This lets the
/// per-section assistant work on any section type — and any future one — without type-specific code:
/// it revises the text at these paths and nothing else (never structure, ids, URLs, or times).
/// </summary>
public static class SectionText
{
    // Keys whose string values are not free prose and must not be rewritten.
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "type", "id", "visible", "mapQuery", "day", "opens", "closes",
    };

    private static bool IsExcluded(string key) =>
        Excluded.Contains(key) || key.EndsWith("Url", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<SectionTextField> Collect(JsonObject section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var fields = new List<SectionTextField>();
        CollectWalk(section, "", fields);
        return fields;
    }

    private static void CollectWalk(JsonNode? node, string path, List<SectionTextField> acc)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (IsExcluded(key))
                    {
                        continue;
                    }
                    CollectWalk(value, path.Length == 0 ? key : $"{path}.{key}", acc);
                }
                break;

            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    CollectWalk(arr[i], $"{path}[{i}]", acc);
                }
                break;

            case JsonValue value when value.TryGetValue<string>(out var text):
                acc.Add(new SectionTextField(path, text));
                break;
        }
    }

    /// <summary>Applies revised text back into the section JSON, but only at paths that already exist.</summary>
    public static void Apply(JsonObject section, IReadOnlyDictionary<string, string> revisions)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(revisions);
        ApplyWalk(section, "", revisions);
    }

    private static void ApplyWalk(JsonNode node, string path, IReadOnlyDictionary<string, string> revisions)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(kv => kv.Key).ToList())
            {
                if (IsExcluded(key))
                {
                    continue;
                }

                var childPath = path.Length == 0 ? key : $"{path}.{key}";
                if (obj[key] is JsonValue v && v.TryGetValue<string>(out _))
                {
                    if (revisions.TryGetValue(childPath, out var revised))
                    {
                        obj[key] = JsonValue.Create(revised);
                    }
                }
                else if (obj[key] is { } child)
                {
                    ApplyWalk(child, childPath, revisions);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            for (var i = 0; i < arr.Count; i++)
            {
                var childPath = $"{path}[{i}]";
                if (arr[i] is JsonValue v && v.TryGetValue<string>(out _))
                {
                    if (revisions.TryGetValue(childPath, out var revised))
                    {
                        arr[i] = JsonValue.Create(revised);
                    }
                }
                else if (arr[i] is { } child)
                {
                    ApplyWalk(child, childPath, revisions);
                }
            }
        }
    }
}
