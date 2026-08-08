namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// The launch set of business categories and the page each one gets (WB-45).
/// <para>
/// The owner types what they do in their own words — "plumber", "chop bar", "hair and nails" — so
/// this maps that free text onto a curated template rather than asking them to pick from a list.
/// A miss is not a failure: <see cref="General"/> is a good generic small-business page, and it is
/// what every site got before this existed.
/// </para>
/// <para>
/// Adding a category is an entry in <see cref="Templates"/> and nothing else. Matching, section
/// building and imagery are all generic over the list — pinned by <c>CategoryTemplateTests</c>, so
/// a category added later cannot quietly need code alongside it.
/// </para>
/// <para>
/// No lineup includes testimonials. Quotes cannot be pre-filled or invented, and a reviews section
/// with nothing in it looks worse than no reviews section at all — the owner adds it from the
/// picker once they have a real one.
/// </para>
/// </summary>
public static class CategoryTemplateCatalog
{
    /// <summary>Imagery: plated food and dining rooms, warm light, no faces to the camera.</summary>
    private static readonly CategoryTemplate Restaurant = new(
        Id: "restaurant",
        Label: "Restaurant, café or food",
        Example: "Restaurant",
        Keywords:
        [
            "restaurant*", "chop bar", "chopbar", "cafe", "café", "coffee", "food", "kitchen*",
            "catering", "caterer*", "baker*", "pastr*", "grill*", "takeaway", "take away",
            "canteen*", "pizza*", "juice", "jollof", "waakye", "fast food", "eatery",
        ],
        Lineup:
        [
            new("hero", ""),
            new("services", "Our menu"),
            new("gallery", "From the kitchen"),
            new("about", "Our story"),
            new("hoursMap", "Where to find us"),
            new("contact", "Get in touch"),
            new("cta", ""),
        ],
        HeroPhoto: new("photo-1414235077428-338989a2e8c0", "A plated dish being served at a restaurant table"),
        AboutPhoto: new("photo-1555396273-367ea4eb4db5", "A bright café interior with wooden tables"),
        GalleryPhotos:
        [
            new("photo-1504674900247-0877df9cc836", "Three dishes laid out on a wooden table"),
            new("photo-1517248135467-4c7edcad34c4", "A dining room set for service"),
            new("photo-1552566626-52f8b828add9", "Booth seating in a warmly lit dining room"),
        ]);

    /// <summary>Imagery: chairs, mirrors and hands at work — the room and the craft, not portraits.</summary>
    private static readonly CategoryTemplate Salon = new(
        Id: "salon",
        Label: "Salon, barber or beauty",
        Example: "Salon & barber",
        Keywords:
        [
            "salon*", "barber*", "hair*", "braid*", "beauty", "spa", "spas", "nail*", "makeup",
            "make-up", "cosmetolog*", "lash*", "dread*", "wig", "wigs", "grooming", "massage*",
        ],
        Lineup:
        [
            new("hero", ""),
            new("services", "Our services"),
            new("gallery", "Our work"),
            new("about", "About {business}"),
            new("hoursMap", "Where to find us"),
            new("contact", "Book an appointment"),
            new("cta", ""),
        ],
        HeroPhoto: new("photo-1521590832167-7bcbfaa6381f", "Styling chairs and mirrors in a salon"),
        AboutPhoto: new("photo-1503951914875-452162b0f3f1", "A barber shaving a client"),
        GalleryPhotos:
        [
            new("photo-1562322140-8baeececf3df", "A stylist blow-drying a client's hair"),
            new("photo-1595476108010-b4d1f102b1b1", "A stylist washing a client's hair at the basin"),
            new("photo-1585747860715-2ba37e788b70", "Barber chairs in a brick-walled shop"),
        ]);

    /// <summary>Imagery: tools and finished work. What the job looks like, not who does it.</summary>
    private static readonly CategoryTemplate Trades = new(
        Id: "trades",
        Label: "Trades and home services",
        Example: "Plumbing",
        Keywords:
        [
            "plumb*", "electric*", "carpent*", "mason*", "weld*", "mechanic*", "fitter*",
            "builder*", "building", "construction", "painter*", "painting", "tiler*", "tiling",
            "roof*", "cleaner*", "cleaning", "fumigat*", "landscap*", "gardener*", "handyman",
            "handywoman", "repair*", "installation*", "air condition*", "aircon*", "generator*",
            "borehole*",
        ],
        Lineup:
        [
            new("hero", ""),
            new("services", "What we do"),
            new("about", "About {business}"),
            new("gallery", "Recent work"),
            new("hoursMap", "Where to find us"),
            new("contact", "Get a quote"),
            new("cta", ""),
        ],
        HeroPhoto: new("photo-1621905251189-08b45d6a269e", "A tradesperson in a hard hat working with a tool"),
        GalleryPhotos:
        [
            new("photo-1607472586893-edb57bdc0e39", "Pipework and valves fixed to a brick wall"),
            new("photo-1581578731548-c64695cc6952", "Cleaning a window with a cloth and gloves"),
            new("photo-1504148455328-c376907d081c", "A cordless drill on a work site"),
        ]);

    /// <summary>
    /// Imagery: desks, notes and meeting rooms. Deliberately no gallery and no portraits — a
    /// stranger's face on a one-person consultancy reads as "this is me", which is the same
    /// invented fact the copy guard exists to stop.
    /// </summary>
    private static readonly CategoryTemplate Consultant = new(
        Id: "consultant",
        Label: "Consultant or professional service",
        Example: "Consulting",
        Keywords:
        [
            "consult*", "coach*", "account*", "bookkeep*", "lawyer*", "legal", "advisor*",
            "adviser*", "tutor*", "teacher*", "trainer*", "training", "therapist*", "counsel*",
            "agency", "marketing", "freelance*", "architect*", "surveyor*", "recruit*",
        ],
        Lineup:
        [
            new("hero", ""),
            new("about", "About {business}"),
            new("services", "How I can help"),
            new("contact", "Get in touch"),
            new("cta", ""),
        ],
        HeroPhoto: new("photo-1454165804606-c3d57bc86b40", "Notes and laptops on a desk during a working session"),
        AboutPhoto: new("photo-1524758631624-e2822e304c36", "Armchairs in a bright meeting room"));

    /// <summary>Imagery: the building and the people it gathers, shot wide rather than close.</summary>
    private static readonly CategoryTemplate Church = new(
        Id: "church",
        Label: "Church or non-profit",
        Example: "Church",
        Keywords:
        [
            "church*", "ministr*", "chapel*", "mosque*", "temple*", "fellowship*",
            "congregation*", "nonprofit*", "non-profit*", "ngo", "ngos", "charity", "charities",
            "foundation*", "outreach", "mission*", "orphanage*", "community group",
        ],
        Lineup:
        [
            new("hero", ""),
            new("about", "Who we are"),
            new("services", "What we do"),
            new("hoursMap", "Service times and location"),
            new("gallery", "Our community"),
            new("contact", "Get in touch"),
            new("cta", ""),
        ],
        HeroPhoto: new("photo-1438032005730-c779502df39b", "Stained glass windows above pews in a church"),
        GalleryPhotos:
        [
            new("photo-1511632765486-a01980e01a18", "Four people watching the sunset together"),
            new("photo-1593113646773-028c64a8f1b8", "Volunteers packing boxes of food"),
        ]);

    /// <summary>Imagery: the room dressed and the moment itself. Photos lead, because the work is visual.</summary>
    private static readonly CategoryTemplate Events = new(
        Id: "events",
        Label: "Events and entertainment",
        Example: "Event planning",
        Keywords:
        [
            // No "planner": a financial planner is a consultant, and "event*" already catches the
            // ones this template is for.
            "event*", "wedding*", "party", "parties", "decor*", "dj", "djs", "photograph*",
            "videograph*", "entertainment", "band", "bands", "rental*", "sound", "usher*", "mc",
            "compere*", "florist*",
        ],
        Lineup:
        [
            new("hero", ""),
            new("gallery", "Past events"),
            new("services", "What we offer"),
            new("about", "About {business}"),
            new("hoursMap", "Where to find us"),
            new("contact", "Check your date"),
            new("cta", ""),
        ],
        HeroPhoto: new("photo-1511795409834-ef04bbd61622", "A long table set with flowers for an event"),
        GalleryPhotos:
        [
            new("photo-1492684223066-81342ee5ff30", "Confetti falling over a crowd at a party"),
            new("photo-1519741497674-611481863552", "A couple holding a bouquet at a wedding"),
            new("photo-1464366400600-7168b8af9bc3", "Tables laid out under bunting at an outdoor event"),
        ]);

    /// <summary>
    /// The page every site got before categories existed, plus a hero photo. Used whenever the
    /// owner's own words don't match anything — which will be often, and must still look finished.
    /// </summary>
    private static readonly CategoryTemplate General = new(
        Id: "general",
        Label: "Small business",
        Example: null,
        Keywords: [],
        Lineup:
        [
            new("hero", ""),
            new("about", "About {business}"),
            new("services", "What we do"),
            new("hoursMap", "Find us"),
            new("contact", "Get in touch"),
            new("cta", ""),
        ],
        HeroPhoto: new("photo-1441986300917-64674bd600d8", "Clothes and shelves in a small shop"),
        AboutPhoto: new("photo-1556742049-0cfed4f6a45d", "A customer paying at a shop counter"));

    public static IReadOnlyList<CategoryTemplate> Templates { get; } =
        [Restaurant, Salon, Trades, Consultant, Church, Events, General];

    /// <summary>The template used when nothing matches.</summary>
    public static CategoryTemplate Fallback => General;

    /// <summary>The suggestion chips shown on the onboarding category step, in catalog order.</summary>
    public static IReadOnlyList<string> Examples { get; } =
        [.. Templates.Select(t => t.Example).OfType<string>()];

    public static CategoryTemplate ById(string id) =>
        Templates.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentOutOfRangeException(nameof(id), id, "No category template with that id.");

    /// <summary>
    /// Picks the template for what the owner typed.
    /// <para>
    /// Keywords match at the start of a word. A trailing <c>*</c> makes one a stem, so
    /// <c>plumb*</c> catches "plumber" and "plumbing"; without it the whole word must match, which
    /// is what stops <c>ngo</c> matching "mango" and <c>spa</c> matching "spare parts".
    /// </para>
    /// <para>
    /// Where two categories both match, the longer keyword wins — so the answer is a property of
    /// what was typed, not of the order of this list.
    /// </para>
    /// </summary>
    public static CategoryTemplate Match(string? category)
    {
        var text = category?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text))
        {
            return Fallback;
        }

        CategoryTemplate? best = null;
        var bestLength = 0;

        foreach (var template in Templates)
        {
            foreach (var keyword in template.Keywords)
            {
                var needle = keyword.EndsWith('*') ? keyword[..^1] : keyword;

                if (needle.Length > bestLength && Matches(text, needle, isStem: needle.Length < keyword.Length))
                {
                    best = template;
                    bestLength = needle.Length;
                }
            }
        }

        return best ?? Fallback;
    }

    private static bool Matches(string text, string needle, bool isStem)
    {
        for (var index = text.IndexOf(needle, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(needle, index + 1, StringComparison.Ordinal))
        {
            var startsWord = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var end = index + needle.Length;
            var endsWord = isStem || end == text.Length || !char.IsLetterOrDigit(text[end]);

            if (startsWord && endsWord)
            {
                return true;
            }
        }

        return false;
    }
}
