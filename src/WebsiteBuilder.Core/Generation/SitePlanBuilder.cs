using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// The prose a page needs, wherever it came from. The template generator fills this from fixed
/// patterns and the assembler fills it from the model's response; neither decides which sections
/// exist.
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

        EnsureUploadedPhotosAreShown(sections, profile);

        return sections;
    }

    /// <summary>
    /// A consultant's lineup has no gallery, because stock photographs of an office say nothing.
    /// Photographs the owner took of their own work are a different matter: having asked for them
    /// and been given them, dropping them on the floor is not an option.
    /// </summary>
    private static void EnsureUploadedPhotosAreShown(List<SiteSection> sections, BusinessProfile profile)
    {
        if (profile.PhotoUrls.Count == 0 || sections.OfType<GallerySection>().Any())
        {
            return;
        }

        var gallery = new GallerySection
        {
            Heading = "Our work",
            Images = profile.PhotoUrls.Select(url => new GalleryImage { Url = url, AltText = "" }).ToList(),
        };

        // Ahead of the closing prompt, so the page still ends by asking for the enquiry.
        var contact = sections.FindIndex(s => s is ContactSection);
        sections.Insert(contact < 0 ? sections.Count : contact, gallery);
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
                // Their first photo above the fold if they gave us one. A real photograph of the
                // actual shop beats the best stock image on the page a customer sees first.
                ImageUrl = profile.PhotoUrls.FirstOrDefault()
                           ?? template.HeroPhoto?.CroppedTo(HeroWidth, HeroHeight),
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

            // The owner's own photos always win. Stock photography exists for the common case
            // where they have none yet: without it the section would be an empty grid, which is
            // worse than no gallery at all.
            "gallery" => Gallery(heading, template, profile),

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

            // A generated site never starts with a shop: there is nothing in the catalog yet, and
            // an empty grid saying "Shop" is worse than no shop. The owner adds it from the picker
            // once they have something to sell.
            "shop" => null,

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

    /// <summary>
    /// The owner's uploaded photos if they have any, otherwise the category's stock set. Alt text
    /// is left blank on their own photos: only they know what is in them, and a guess would be
    /// read aloud to someone using a screen reader as though it were fact.
    /// </summary>
    private static GallerySection? Gallery(string heading, CategoryTemplate template, BusinessProfile profile)
    {
        if (profile.PhotoUrls.Count > 0)
        {
            return new GallerySection
            {
                Heading = heading,
                Images = profile.PhotoUrls
                    .Select(url => new GalleryImage { Url = url, AltText = "" })
                    .ToList(),
            };
        }

        return template.Gallery.Count == 0 ? null : new GallerySection
        {
            Heading = heading,
            Images = template.Gallery
                .Select(photo => new GalleryImage
                {
                    Url = photo.CroppedTo(GalleryWidth, GalleryHeight),
                    AltText = photo.AltText,
                })
                .ToList(),
        };
    }

    private static string Heading(SectionSlot slot, BusinessProfile profile) =>
        slot.Heading.Replace("{business}", profile.BusinessName, StringComparison.Ordinal);
}
