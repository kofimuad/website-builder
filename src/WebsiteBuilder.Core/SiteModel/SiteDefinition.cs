namespace WebsiteBuilder.Core.SiteModel;

/// <summary>
/// A complete snapshot of one site: metadata, theme and ordered sections. Drafts and published
/// snapshots are both stored as this shape, so the renderer never cares which one it was handed.
/// </summary>
public sealed class SiteDefinition
{
    /// <summary>Bumped whenever the stored shape changes; see docs/site-schema.md.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public SiteMeta Meta { get; set; } = new();
    public SiteTheme Theme { get; set; } = new();
    public List<SiteSection> Sections { get; set; } = [];

    /// <summary>
    /// Independent copy, so publishing a draft cannot leave the live site sharing objects with
    /// the draft that is still being edited.
    /// </summary>
    public SiteDefinition DeepClone() => SiteDefinitionSerializer.Deserialize(SiteDefinitionSerializer.Serialize(this));
}

public sealed class SiteMeta
{
    public string BusinessName { get; set; } = "";
    public string? Tagline { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
}
