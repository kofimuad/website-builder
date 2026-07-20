using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Core.Onboarding;

/// <summary>How the owner wants the site to read. Drives copy and palette during generation.</summary>
public enum BusinessTone
{
    Friendly,
    Professional,
    Bold,
}

/// <summary>The one thing the site should push visitors towards.</summary>
public enum PrimaryAction
{
    Call,
    Message,
    Visit,
    Book,
}

/// <summary>
/// The answers from onboarding, kept as a first-class record rather than being folded straight
/// into a site: generation reads it, the owner can edit it later (WB-18), and regenerating must
/// not need the interview to be taken again.
/// </summary>
public class BusinessProfile : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string BusinessName { get; set; } = "";

    /// <summary>Free text in the owner's own words, e.g. "plumber", "hair salon".</summary>
    public string Category { get; set; } = "";

    /// <summary>One line per thing they offer, as typed.</summary>
    public List<string> Offerings { get; set; } = [];

    public BusinessTone Tone { get; set; } = BusinessTone.Friendly;
    public PrimaryAction PrimaryAction { get; set; } = PrimaryAction.Call;

    public string? PhoneNumber { get; set; }
    public string? WhatsAppNumber { get; set; }
    public string? Email { get; set; }

    public List<string> AddressLines { get; set; } = [];

    /// <summary>Where they work, in their words: "Osu and East Legon", "all of Accra".</summary>
    public string? ServiceArea { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
