namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// The prose the model is asked to write. Deliberately holds no facts — contact details,
/// addresses, service area and section ordering are supplied by code from the business profile,
/// so the model cannot invent them (see <see cref="SiteContentAssembler"/>).
/// </summary>
public sealed class GeneratedSiteContent
{
    public string HeroHeadline { get; set; } = "";
    public string HeroSubheadline { get; set; } = "";
    public string AboutHeading { get; set; } = "";
    public string AboutBody { get; set; } = "";
    public List<GeneratedService> Services { get; set; } = [];
    public string CtaHeadline { get; set; } = "";
    public string CtaButtonLabel { get; set; } = "";
    public string SeoTitle { get; set; } = "";
    public string SeoDescription { get; set; } = "";
    public string Tagline { get; set; } = "";

    /// <summary>One of the curated palette names: friendly, professional, or bold.</summary>
    public string Palette { get; set; } = "friendly";
}

public sealed class GeneratedService
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}
