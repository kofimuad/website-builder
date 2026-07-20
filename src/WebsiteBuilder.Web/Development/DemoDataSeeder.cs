using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;

namespace WebsiteBuilder.Web.Development;

/// <summary>
/// Seeds one published demo site so the renderer has something to show before onboarding
/// (WB-2) exists. Development only, and does nothing if the tenant is already there.
/// </summary>
public static class DemoDataSeeder
{
    public const string DemoSubdomain = "joesplumbing";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();

        if (await db.Tenants.AnyAsync(t => t.Subdomain == DemoSubdomain, cancellationToken))
        {
            return;
        }

        var tenant = new Tenant { Subdomain = DemoSubdomain, Name = "Joe's Plumbing" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);

        // Sites are tenant-owned, so the tenant has to be in scope before one can be saved.
        tenantContext.TenantId = tenant.Id;

        var site = new Site { Name = "Main site", Draft = BuildDemoSite() };
        site.Publish();
        db.Sites.Add(site);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static SiteDefinition BuildDemoSite() => new()
    {
        Meta = new SiteMeta
        {
            BusinessName = "Joe's Plumbing",
            Tagline = "Family-run plumbers serving Accra since 1998",
            SeoTitle = "Joe's Plumbing — emergency plumbers in Accra",
            SeoDescription = "Blocked drains, leaks and boiler repairs across Accra. Same-day callouts.",
        },
        Theme = new SiteTheme
        {
            Palette = new ColorPalette
            {
                Primary = "#0a7d55",
                Accent = "#f2a900",
                Background = "#ffffff",
                Surface = "#f1f7f4",
                Text = "#15211c",
                MutedText = "#5a6b64",
            },
            Fonts = new FontPair { Heading = "Georgia", Body = "system-ui" },
        },
        Sections =
        [
            new HeroSection
            {
                Headline = "Blocked drain? We come today.",
                Subheadline = "Family-run plumbers serving Accra since 1998. No callout fee.",
                CallToActionLabel = "Call now",
                CallToActionUrl = "tel:+233200000000",
            },
            new AboutSection
            {
                Heading = "About us",
                Body = "Joe started fixing pipes in Osu almost thirty years ago, and the workshop is still "
                     + "on the same street.\nToday the team is six plumbers, all trained in-house, and we "
                     + "still answer the phone ourselves.",
            },
            new ServicesSection
            {
                Heading = "What we do",
                Items =
                [
                    new ServiceItem { Title = "Drain clearing", Description = "Cleared fast, no mess left behind.", PriceLabel = "from GHS 200" },
                    new ServiceItem { Title = "Leak detection", Description = "Found and fixed on the same visit." },
                    new ServiceItem { Title = "Bathroom fitting", Description = "Full installs, quoted up front.", PriceLabel = "quoted per job" },
                ],
            },
            new TestimonialsSection
            {
                Heading = "What our customers say",
                Items =
                [
                    new Testimonial { Quote = "Came out on a Sunday and had it sorted in an hour.", AuthorName = "Ama D.", AuthorDetail = "Osu" },
                    new Testimonial { Quote = "Honest pricing, no surprises on the bill.", AuthorName = "Kwesi B.", AuthorDetail = "Labone" },
                ],
            },
            new HoursMapSection
            {
                Heading = "Find us",
                AddressLines = ["12 High Street", "Osu, Accra"],
                MapQuery = "Osu, Accra",
                OpeningHours =
                [
                    new OpeningHours { Day = DayOfWeek.Monday, Opens = new TimeOnly(8, 0), Closes = new TimeOnly(17, 30) },
                    new OpeningHours { Day = DayOfWeek.Tuesday, Opens = new TimeOnly(8, 0), Closes = new TimeOnly(17, 30) },
                    new OpeningHours { Day = DayOfWeek.Wednesday, Opens = new TimeOnly(8, 0), Closes = new TimeOnly(17, 30) },
                    new OpeningHours { Day = DayOfWeek.Thursday, Opens = new TimeOnly(8, 0), Closes = new TimeOnly(17, 30) },
                    new OpeningHours { Day = DayOfWeek.Friday, Opens = new TimeOnly(8, 0), Closes = new TimeOnly(16, 0) },
                    new OpeningHours { Day = DayOfWeek.Saturday, Opens = new TimeOnly(9, 0), Closes = new TimeOnly(13, 0) },
                    new OpeningHours { Day = DayOfWeek.Sunday, Closed = true },
                ],
            },
            new ContactSection
            {
                Heading = "Get in touch",
                Email = "hello@joesplumbing.example",
                PhoneNumber = "+233200000000",
                WhatsAppNumber = "+233200000000",
            },
            new CtaSection
            {
                Headline = "Need a plumber today?",
                ButtonLabel = "Call Joe's Plumbing",
                ButtonUrl = "tel:+233200000000",
            },
        ],
    };
}
