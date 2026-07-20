using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WebsiteBuilder.Core.SiteModel;

/// <summary>
/// Reads and writes sections by their <c>type</c> discriminator.
/// <para>
/// This exists instead of <c>[JsonPolymorphic]</c> because definitions live in Postgres
/// <c>jsonb</c>, which does not preserve key order — it re-sorts object keys on storage. The
/// built-in polymorphic reader needs the discriminator to be the first property, so a definition
/// written through it would fail to load back out of the database. Looking the discriminator up
/// by name makes reads independent of key order.
/// </para>
/// </summary>
public sealed class SiteSectionJsonConverter : JsonConverter<SiteSection>
{
    /// <summary>
    /// Discriminator values are persisted in customer data; changing one is a schema migration.
    /// See docs/site-schema.md.
    /// </summary>
    private static readonly Dictionary<string, Type> TypesByDiscriminator = new(StringComparer.Ordinal)
    {
        ["hero"] = typeof(HeroSection),
        ["about"] = typeof(AboutSection),
        ["services"] = typeof(ServicesSection),
        ["gallery"] = typeof(GallerySection),
        ["testimonials"] = typeof(TestimonialsSection),
        ["contact"] = typeof(ContactSection),
        ["hoursMap"] = typeof(HoursMapSection),
        ["cta"] = typeof(CtaSection),
    };

    private static readonly Dictionary<Type, string> DiscriminatorsByType =
        TypesByDiscriminator.ToDictionary(pair => pair.Value, pair => pair.Key);

    private const string DiscriminatorProperty = "type";

    /// <summary>Used for the concrete type itself; excludes this converter so reads and writes do not recurse.</summary>
    private static readonly JsonSerializerOptions ConcreteTypeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public override SiteSection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (JsonNode.Parse(ref reader) is not JsonObject node)
        {
            throw new JsonException("A site section must be a JSON object.");
        }

        var discriminator = node[DiscriminatorProperty]?.GetValue<string>()
            ?? throw new JsonException($"A site section is missing its '{DiscriminatorProperty}' discriminator.");

        if (!TypesByDiscriminator.TryGetValue(discriminator, out var concreteType))
        {
            throw new JsonException($"Unknown site section type '{discriminator}'.");
        }

        node.Remove(DiscriminatorProperty);

        return (SiteSection)node.Deserialize(concreteType, ConcreteTypeOptions)!;
    }

    public override void Write(Utf8JsonWriter writer, SiteSection value, JsonSerializerOptions options)
    {
        var runtimeType = value.GetType();

        if (!DiscriminatorsByType.TryGetValue(runtimeType, out var discriminator))
        {
            throw new JsonException(
                $"Site section type '{runtimeType.Name}' has no discriminator; add it to {nameof(SiteSectionJsonConverter)}.");
        }

        var node = JsonSerializer.SerializeToNode(value, runtimeType, ConcreteTypeOptions)!.AsObject();

        // Written first for readability; jsonb will re-sort it on storage regardless.
        var result = new JsonObject { [DiscriminatorProperty] = discriminator };
        foreach (var property in node.ToList())
        {
            node.Remove(property.Key);
            result[property.Key] = property.Value;
        }

        result.WriteTo(writer);
    }
}
