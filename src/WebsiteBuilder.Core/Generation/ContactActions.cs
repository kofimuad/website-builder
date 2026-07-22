using WebsiteBuilder.Core.Onboarding;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// Turns a business profile's chosen primary action into a button label and a working link.
/// The link falls through whatever contact details exist, so a call-to-action is never dead.
/// </summary>
public static class ContactActions
{
    public static string DefaultLabel(PrimaryAction action) => action switch
    {
        PrimaryAction.Visit => "Get directions",
        PrimaryAction.Book => "Book now",
        PrimaryAction.Message => "Message us",
        _ => "Call us",
    };

    public static string ResolveUrl(BusinessProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var phone = profile.PhoneNumber;
        var whatsApp = profile.WhatsAppNumber;

        return profile.PrimaryAction switch
        {
            PrimaryAction.Message when !string.IsNullOrWhiteSpace(whatsApp) => WhatsAppUrl(whatsApp),
            PrimaryAction.Visit when profile.AddressLines.Count > 0 =>
                "https://maps.google.com/maps?q=" + Uri.EscapeDataString(string.Join(", ", profile.AddressLines)),
            _ when !string.IsNullOrWhiteSpace(phone) => $"tel:{phone}",
            _ when !string.IsNullOrWhiteSpace(whatsApp) => WhatsAppUrl(whatsApp),
            _ when !string.IsNullOrWhiteSpace(profile.Email) => $"mailto:{profile.Email}",
            _ => "#contact",
        };
    }

    private static string WhatsAppUrl(string number) => $"https://wa.me/{number.TrimStart('+')}";
}
