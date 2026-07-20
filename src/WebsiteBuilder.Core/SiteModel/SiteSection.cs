namespace WebsiteBuilder.Core.SiteModel;

/// <summary>
/// One block of a page. Sections are mapped to and from JSON by
/// <see cref="SiteSectionJsonConverter"/>, which also owns the list of discriminator values.
/// </summary>
public abstract class SiteSection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Hidden sections stay in the draft but are skipped by the renderer.</summary>
    public bool Visible { get; set; } = true;
}

public sealed class HeroSection : SiteSection
{
    public string Headline { get; set; } = "";
    public string Subheadline { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string? CallToActionLabel { get; set; }
    public string? CallToActionUrl { get; set; }
}

public sealed class AboutSection : SiteSection
{
    public string Heading { get; set; } = "";
    public string Body { get; set; } = "";
    public string? ImageUrl { get; set; }
}

public sealed class ServicesSection : SiteSection
{
    public string Heading { get; set; } = "";
    public List<ServiceItem> Items { get; set; } = [];
}

public sealed class ServiceItem
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? PriceLabel { get; set; }
}

public sealed class GallerySection : SiteSection
{
    public string Heading { get; set; } = "";
    public List<GalleryImage> Images { get; set; } = [];
}

public sealed class GalleryImage
{
    public string Url { get; set; } = "";
    public string AltText { get; set; } = "";
    public string? Caption { get; set; }
}

public sealed class TestimonialsSection : SiteSection
{
    public string Heading { get; set; } = "";
    public List<Testimonial> Items { get; set; } = [];
}

public sealed class Testimonial
{
    public string Quote { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string? AuthorDetail { get; set; }
}

public sealed class ContactSection : SiteSection
{
    public string Heading { get; set; } = "";
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? WhatsAppNumber { get; set; }

    /// <summary>Whether the lead-capture form is shown (leads themselves are WB-5).</summary>
    public bool ShowEnquiryForm { get; set; } = true;
}

public sealed class HoursMapSection : SiteSection
{
    public string Heading { get; set; } = "";
    public List<string> AddressLines { get; set; } = [];

    /// <summary>Free-text location used to build the map embed, e.g. "12 High St, Accra".</summary>
    public string? MapQuery { get; set; }

    public List<OpeningHours> OpeningHours { get; set; } = [];
}

public sealed class OpeningHours
{
    public DayOfWeek Day { get; set; }
    public bool Closed { get; set; }
    public TimeOnly? Opens { get; set; }
    public TimeOnly? Closes { get; set; }
}

public sealed class CtaSection : SiteSection
{
    public string Headline { get; set; } = "";
    public string ButtonLabel { get; set; } = "";
    public string ButtonUrl { get; set; } = "";
}
