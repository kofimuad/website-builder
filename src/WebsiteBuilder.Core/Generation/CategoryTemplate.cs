namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// A curated stock photo, held as an Unsplash photo id rather than a finished URL. The width is
/// chosen per slot — a hero and a gallery thumbnail should not download the same file over a
/// Ghanaian mobile connection — and keeping the id separate from the URL means moving the whole
/// library onto Cloudinary later is a change to one method, not to seven templates.
/// </summary>
public sealed record StockPhoto(string PhotoId, string AltText)
{
    /// <summary>
    /// Fits inside <paramref name="width"/> without cropping — the counterpart of
    /// <c>ImageDelivery.Sized</c>, which is what the renderer applies to an uploaded photo in the
    /// same slot.
    /// </summary>
    public string UrlAt(int width) => Url($"fit=max&w={width}");

    /// <summary>
    /// Fills an exact box, as <c>ImageDelivery.Cropped</c> does for uploads. The shape has to be
    /// baked in here: the renderer's crop only applies to Cloudinary URLs, so without it a gallery
    /// of stock photos would arrive in three different aspect ratios and be cropped by CSS after
    /// the full-size bytes had already been paid for.
    /// </summary>
    public string CroppedTo(int width, int height) => Url($"fit=crop&w={width}&h={height}");

    /// <summary>
    /// <c>auto=format</c> serves WebP to browsers that accept it, which is most of them, and
    /// <c>q=70</c> is the point where further compression starts to show on photographs.
    /// </summary>
    private string Url(string sizing) =>
        $"https://images.unsplash.com/{PhotoId}?auto=format&{sizing}&q=70";
}

/// <summary>
/// One block in a category's default page: which section type, and what that category calls it.
/// A restaurant's list of offerings is a menu; a plumber's is a list of services.
/// </summary>
/// <param name="Kind">A section discriminator — see <c>SiteSectionJsonConverter</c>.</param>
/// <param name="Heading">
/// The heading, in which <c>{business}</c> is replaced with the business name. Empty for hero and
/// cta, which have no heading of their own — their words are copy, not structure.
/// </param>
public sealed record SectionSlot(string Kind, string Heading);

/// <summary>
/// The default page for one kind of business: the order of its sections, what each is called, and
/// the photographs to fall back on until the owner uploads their own.
/// </summary>
/// <param name="Example">
/// The short wording offered as a suggestion chip during onboarding, or null for a template
/// nobody would type. Onboarding reads these from the catalog, so a category added here appears
/// in the picker without touching the wizard.
/// </param>
public sealed record CategoryTemplate(
    string Id,
    string Label,
    string? Example,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<SectionSlot> Lineup,
    StockPhoto? HeroPhoto = null,
    StockPhoto? AboutPhoto = null,
    IReadOnlyList<StockPhoto>? GalleryPhotos = null)
{
    public IReadOnlyList<StockPhoto> Gallery => GalleryPhotos ?? [];
}
