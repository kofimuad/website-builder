using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Core.Onboarding;

/// <summary>
/// Flows a business profile's contact-type facts into a draft site's sections. Called when the
/// owner edits their profile (WB-18): the name, contact details, address and hours propagate to
/// the relevant sections without regenerating any copy. Only the draft is touched.
/// </summary>
public static class ProfileToDraft
{
    public static void Apply(BusinessProfile profile, SiteDefinition draft)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(draft);

        draft.Meta.BusinessName = profile.BusinessName;

        ApplyContact(profile, draft);
        ApplyHoursAndAddress(profile, draft);
    }

    private static void ApplyContact(BusinessProfile profile, SiteDefinition draft)
    {
        var contact = draft.Sections.OfType<ContactSection>().FirstOrDefault();
        if (contact is null)
        {
            return;
        }

        contact.PhoneNumber = profile.PhoneNumber;
        contact.WhatsAppNumber = profile.WhatsAppNumber;
        contact.Email = profile.Email;
    }

    private static void ApplyHoursAndAddress(BusinessProfile profile, SiteDefinition draft)
    {
        var hasLocationInfo = profile.AddressLines.Count > 0 || profile.OpeningHours.Count > 0;
        var section = draft.Sections.OfType<HoursMapSection>().FirstOrDefault();

        if (section is null)
        {
            // Nothing to show and no section: leave the site as it is.
            if (!hasLocationInfo)
            {
                return;
            }

            // The owner added an address or hours where there was none — give them a section,
            // placed just before the contact section so location and contact sit together.
            section = new HoursMapSection { Heading = "Find us" };
            var contactIndex = draft.Sections.FindIndex(s => s is ContactSection);
            if (contactIndex >= 0)
            {
                draft.Sections.Insert(contactIndex, section);
            }
            else
            {
                draft.Sections.Add(section);
            }
        }

        section.AddressLines = [.. profile.AddressLines];
        section.MapQuery = profile.AddressLines.Count > 0 ? string.Join(", ", profile.AddressLines) : null;
        section.OpeningHours = [.. profile.OpeningHours];
    }
}
