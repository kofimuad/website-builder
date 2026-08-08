namespace WebsiteBuilder.Web.Platform;

/// <summary>
/// The product's name, in one place.
///
/// It has already changed once — "Sitely" turned out to belong to somebody else — and that sweep
/// touched nine files and two cookie names. Anything a customer can read takes the name from here
/// so the next change is one line rather than a hunt.
///
/// The domain is deliberately not here: it is configuration
/// (<see cref="Core.Tenancy.TenantResolutionOptions.PlatformDomain"/>), because it differs between
/// development and production while the name does not.
/// </summary>
public static class Branding
{
    public const string Name = "CS Build";

    public const string Tagline = "Websites for small businesses";

    /// <summary>
    /// Prefix for our own cookies. Separate from <see cref="Name"/> on purpose: it has to be a
    /// valid cookie name, and changing it signs every owner out, so it should move only when
    /// somebody means it to.
    /// </summary>
    public const string CookiePrefix = "csbuild";
}
