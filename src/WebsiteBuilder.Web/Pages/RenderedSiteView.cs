using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Web.Pages;

/// <summary>
/// Everything the site renderer draws: the published document plus the live rows it references.
/// <para>
/// The shop is the reason this exists. A <c>ShopSection</c> says only where the catalog goes; the
/// products themselves are relational and current, so they have to be handed to the view alongside
/// the definition rather than read out of it.
/// </para>
/// </summary>
public sealed record RenderedSiteView(
    SiteDefinition Definition,
    IReadOnlyList<Product> Products)
{
    /// <summary>A definition with no shop to fill — the preview, and every site without a catalog.</summary>
    public static RenderedSiteView Of(SiteDefinition definition) => new(definition, []);
}
