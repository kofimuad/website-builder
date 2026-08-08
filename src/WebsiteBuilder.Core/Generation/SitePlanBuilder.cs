using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// The prose a page needs, wherever it came from. The template generator fills this from fixed
/// patterns and Claude fills it from the model's response; neither decides which sections exist.
/// </summary>
public sealed record SiteCopy(
    string HeroHeadline,
    string HeroSubheadline,
    string AboutBody,
    string CtaHeadline,
    string CtaButtonLabel,
    string AboutHeading = "",
    IReadOnlyDictionary<string, string>? ServiceDescriptions = null)
{
    public string DescriptionFor(string title) =>
        ServiceDescriptions is not null && ServiceDescriptions.TryGetValue(title.Trim(), out var description)
            ? description
            : "";
}

/// <summary>
/// Turns a category template plus a profile plus some copy into the sections of a page.
/// <para>
/// Both generators come through here, which is the point: the shape of a page is a property of the
/// business category, not of whether a model happened to be reachable. Before this existed the two
/// generators each built their own lineup, and the two drifted.
/// </para>
/// </summary>
public static class SitePlanBuilder
{
    // The sizes _RenderedSite.cshtml asks for in each slot. Keeping them in step means the photo
    // that arrives is the photo that is shown, with nothing downloaded to be thrown away.
    private const int HeroWidth = 1600;
    private const int HeroHeight = 900;
    private const int AboutWidth = 1200;
    private const int GalleryWidth = 800;
    private const int GalleryHeight = 600;

    public static List<SiteSection> Build(CategoryTemplate template, BusinessProfile profile, SiteCopy copy)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(copy);

        var sections = new List<SiteSection>();

        foreach (var slot in template.Lineup)
        {
            var section = BuildSlot(slot, template, profile, copy);
            if (section is not null)
            {
                sections.Add(section);
            }
        }

        return sections;
    }

    /// <summary>
    /// Returns null for a slot this business cannot fill. An empty heading with nothing under it
    /// looks broken, and the owner would have to work out that deleting it is the fix.
    /// </summary>
    private static SiteSection? BuildSlot(
        SectionSlot slot,
        CategoryTemplate template,
        BusinessProfile profile,
        SiteCopy copy)
    {
        var heading = Heading(slot, profile);

        return slot.Kind switch
        {
            "hero" => new HeroSection
            {
                Headline = copy.HeroHeadline,
                Subheadline = copy.HeroSubheadline,
                ImageUrl = template.HeroPhoto?.CroppedTo(HeroWidth, HeroHeight),
                CallToActionLabel = copy.CtaButtonLabel,
                CallToActionUrl = ContactActions.ResolveUrl(profile),
            },

            // The model is allowed to name this section if it wants to; the category's own heading
            // is the default rather than the rule, because "Our story" is a suggestion about a
            // restaurant and the model may know better about this one.
            "about" => new AboutSection
            {
                Heading = string.IsNullOrWhiteSpace(copy.AboutHeading) ? heading : copy.AboutHeading,
                Body = copy.AboutBody,
                ImageUrl = template.AboutPhoto?.UrlAt(AboutWidth),
            },

            "services" => profile.Offerings.Count == 0 ? null : new ServicesSection
            {
                Heading = heading,
                Items = profile.Offerings
                    .Select(offering => new ServiceItem
                    {
                        Title = offering,
                        Description = copy.DescriptionFor(offering),
                    })
                    .ToList(),
            },

            // Stock photography is the whole reason a gallery can appear at all: at onboarding the
            // owner has uploaded nothing, so without a fallback the section would be an empty grid.
            "gallery" => template.Gallery.Count == 0 ? null : new GallerySection
            {
                Heading = heading,
                Images = template.Gallery
                    .Select(photo => new GalleryImage
                    {
                        Url = photo.CroppedTo(GalleryWidth, GalleryHeight),
                        AltText = photo.AltText,
                    })
                    .ToList(),
            },

            "hoursMap" => profile.AddressLines.Count == 0 ? null : new HoursMapSection
            {
                Heading = heading,
                AddressLines = [.. profile.AddressLines],
                MapQuery = string.Join(", ", profile.AddressLines),
            },

            "contact" => new ContactSection
            {
                Heading = heading,
                PhoneNumber = profile.PhoneNumber,
                WhatsAppNumber = profile.WhatsAppNumber,
                Email = profile.Email,
            },

            "cta" => new CtaSection
            {
                Headline = copy.CtaHeadline,
                ButtonLabel = copy.CtaButtonLabel,
                ButtonUrl = ContactActions.ResolveUrl(profile),
            },

            _ => throw new InvalidOperationException(
                $"Category template '{template.Id}' asks for section kind '{slot.Kind}', which no " +
                "generator knows how to build. Add it here or correct the template."),
        };
    }

    private static string Heading(SectionSlot slot, BusinessProfile profile) =>
        slot.Heading.Replace("{business}", profile.BusinessName, StringComparison.Ordinal);
}
