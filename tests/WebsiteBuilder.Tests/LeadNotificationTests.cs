using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Leads;
using WebsiteBuilder.Web.Platform;

namespace WebsiteBuilder.Tests;

/// <summary>
/// Notifying the owner when an enquiry arrives (WB-32). The interesting cases are the ones where
/// email is not possible: the lead is already saved by then, so nothing here may throw.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class LeadNotificationTests(PostgresFixture fixture)
{
    private static (EmailLeadNotifier Notifier, CapturingEmailSender Email) Create(WebsiteBuilderDbContext db)
    {
        var email = new CapturingEmailSender();

        var urls = new PlatformUrls(
            new HttpContextAccessor(),
            Options.Create(new PlatformOptions { PublicBaseUrl = "https://sitely.test" }),
            Options.Create(new TenantResolutionOptions()));

        return (new EmailLeadNotifier(db, email, urls, NullLogger<EmailLeadNotifier>.Instance), email);
    }

    private async Task<(Guid TenantId, Guid SiteId, string OwnerEmail)> SeedAsync()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@example.com";

        using var db = fixture.CreateContext(new TenantContext());
        var owner = new Owner { Email = ownerEmail, Name = "Joe" };
        db.Owners.Add(owner);

        var tenant = new Tenant
        {
            Subdomain = $"t{Guid.NewGuid():N}"[..12],
            Name = "Joe's Plumbing",
            OwnerId = owner.Id,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        using var scoped = fixture.CreateContext(tenant.Id);
        var site = new Site { Name = "Main site" };
        scoped.Sites.Add(site);
        await scoped.SaveChangesAsync();

        return (tenant.Id, site.Id, ownerEmail);
    }

    [Fact]
    public async Task A_new_lead_is_emailed_to_the_business_owner()
    {
        var (tenantId, siteId, ownerEmail) = await SeedAsync();

        using var db = fixture.CreateContext(tenantId);
        var (notifier, email) = Create(db);

        await notifier.NotifyAsync(new Lead
        {
            TenantId = tenantId,
            SiteId = siteId,
            Name = "Ama Boateng",
            PhoneNumber = "+233209999999",
            Message = "Blocked kitchen drain, need someone today",
        });

        var sent = Assert.Single(email.Sent);
        Assert.Equal(ownerEmail, sent.To);
        Assert.Contains("Ama Boateng", sent.Subject);
        Assert.Contains("Blocked kitchen drain", sent.TextBody);
        Assert.Contains("+233209999999", sent.TextBody);
        Assert.Contains($"https://sitely.test/manage/{siteId}/leads", sent.TextBody);
    }

    [Fact]
    public async Task Lead_text_is_escaped_in_the_html_body()
    {
        var (tenantId, siteId, _) = await SeedAsync();

        using var db = fixture.CreateContext(tenantId);
        var (notifier, email) = Create(db);

        await notifier.NotifyAsync(new Lead
        {
            TenantId = tenantId,
            SiteId = siteId,
            Name = "<script>alert(1)</script>",
            PhoneNumber = "+233200000000",
            Message = "Quote for <b>everything</b>",
        });

        var sent = Assert.Single(email.Sent);

        // A contact form is open to anyone; its text must not become live markup in the owner's inbox.
        Assert.DoesNotContain("<script>", sent.HtmlBody);
        Assert.Contains("&lt;script&gt;", sent.HtmlBody);
        Assert.Contains("&lt;b&gt;everything&lt;/b&gt;", sent.HtmlBody);
    }

    [Fact]
    public async Task A_tenant_with_no_owner_is_skipped_without_failing()
    {
        using var setup = fixture.CreateContext(new TenantContext());
        var tenant = new Tenant { Subdomain = $"t{Guid.NewGuid():N}"[..12], Name = "Ownerless", OwnerId = null };
        setup.Tenants.Add(tenant);
        await setup.SaveChangesAsync();

        using var db = fixture.CreateContext(tenant.Id);
        var (notifier, email) = Create(db);

        // Seeded and pre-sign-in tenants have no owner. The lead is still in the inbox.
        await notifier.NotifyAsync(new Lead
        {
            TenantId = tenant.Id,
            SiteId = Guid.NewGuid(),
            Name = "Nobody",
            Message = "Hello",
        });

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task A_mail_provider_failure_does_not_reach_the_visitor()
    {
        var (tenantId, siteId, _) = await SeedAsync();

        using var db = fixture.CreateContext(tenantId);
        var (notifier, email) = Create(db);
        email.ThrowOnSend = true;

        // The lead is committed before this runs, so a bounced notification must not surface as an
        // error on the contact form of a site the visitor is trying to use.
        await notifier.NotifyAsync(new Lead
        {
            TenantId = tenantId,
            SiteId = siteId,
            Name = "Ama",
            PhoneNumber = "+233200000000",
            Message = "Hello",
        });
    }
}
