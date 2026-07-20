using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Core.Entities;

public class Site : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>What the owner is editing. Never served to visitors.</summary>
    public SiteDefinition Draft { get; set; } = new();

    /// <summary>What visitors see. Null until the site is published for the first time.</summary>
    public SiteDefinition? Published { get; set; }

    public DateTimeOffset? PublishedUtc { get; set; }

    public bool IsPublished => Published is not null;

    /// <summary>
    /// Copies the draft over the live site. The copy is deep: without it the two snapshots would
    /// share section objects and the next draft edit would silently change the published site.
    /// </summary>
    public void Publish(TimeProvider? timeProvider = null)
    {
        Published = Draft.DeepClone();
        PublishedUtc = (timeProvider ?? TimeProvider.System).GetUtcNow();
    }

    /// <summary>Throws away draft edits and returns to what is currently live.</summary>
    public void DiscardDraft()
    {
        if (Published is null)
        {
            throw new InvalidOperationException("Cannot discard the draft of a site that has never been published.");
        }

        Draft = Published.DeepClone();
    }
}
