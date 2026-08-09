using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.Generation;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Publishing;

namespace WebsiteBuilder.Tests;

[Collection(nameof(PostgresCollection))]
public class SiteRendererTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private static SiteDefinition SampleDefinition() => new()
    {
        Meta = new SiteMeta
        {
            BusinessName = "Joe's Plumbing",
            SeoTitle = "Joe's Plumbing — Accra",
            SeoDescription = "Emergency plumbing across Accra, same day.",
        },
        Theme = new SiteTheme { Palette = new ColorPalette { Primary = "#0a7d55" } },
        Sections =
        [
            new HeroSection { Headline = "Blocked drain?", Subheadline = "We come today" },
            new ServicesSection
            {
                Heading = "What we do",
                Items = [new ServiceItem { Title = "Drain clearing", Description = "Cleared fast" }],
            },
            new ContactSection { Heading = "Get in touch", PhoneNumber = "+233200000000", Email = "joe@example.com" },
            new CtaSection { Headline = "Ready?", ButtonLabel = "Book now", ButtonUrl = "/contact" },
        ],
    };

    /// <summary>Creates a tenant with one site, published unless told otherwise.</summary>
    private async Task<(string Subdomain, Guid SiteId, Guid TenantId)> SeedSiteAsync(bool publish = true, SiteDefinition? definition = null)
    {
        var subdomain = $"r{Guid.NewGuid():N}"[..12];

        using var scope = _factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

        var tenant = new Tenant { Subdomain = subdomain, Name = "Render Tenant" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        tenantContext.TenantId = tenant.Id;
        var site = new Site { Name = "Site", Draft = definition ?? SampleDefinition() };
        if (publish)
        {
            site.Publish();
        }

        db.Sites.Add(site);
        await db.SaveChangesAsync();

        return (subdomain, site.Id, tenant.Id);
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    /// <summary>
    /// Razor encodes characters such as '+' as numeric entities, which browsers decode. Assert
    /// against the decoded text so the tests describe what a visitor sees.
    /// </summary>
    private static string Decode(string html) => WebUtility.HtmlDecode(html);

    [Fact]
    public async Task A_published_site_renders_its_content()
    {
        var (subdomain, _, _) = await SeedSiteAsync();

        var response = await CreateClient().GetAsync($"http://{subdomain}.platform.com/");
        var html = Decode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Blocked drain?", html);
        Assert.Contains("Drain clearing", html);
        Assert.Contains("Book now", html);
        Assert.Contains("tel:+233200000000", html);
    }

    [Fact]
    public async Task The_rendered_page_carries_seo_metadata_and_a_mobile_viewport()
    {
        var (subdomain, _, _) = await SeedSiteAsync();

        var html = Decode(await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/"));

        Assert.Contains("<title>Joe's Plumbing — Accra</title>", html);
        Assert.Contains("Emergency plumbing across Accra", html);
        Assert.Contains("width=device-width", html);
    }

    [Fact]
    public async Task Preview_nav_links_stay_inside_the_preview()
    {
        // Nav targets are anchors on the home page. Written as "/#services" they threw the owner
        // out of the preview and onto the marketing site the moment they clicked one.
        var ownerId = await _factory.CreateOwnerAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

            var tenant = new Tenant { Subdomain = $"v{Guid.NewGuid():N}"[..12], Name = "Preview", OwnerId = ownerId };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            tenantContext.TenantId = tenant.Id;
            db.Sites.Add(new Site { Name = "Site", Draft = SampleDefinition() });
            await db.SaveChangesAsync();
        }

        Guid siteId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            siteId = await db.Sites.IgnoreQueryFilters()
                .Join(db.Tenants.Where(t => t.OwnerId == ownerId), s => s.TenantId, t => t.Id, (s, _) => s.Id)
                .FirstAsync();
        }

        var html = Decode(await _factory.CreateClientAs(ownerId).GetStringAsync($"http://platform.com/preview/{siteId}"));

        Assert.Contains($"/preview/{siteId}#contact", html);
        Assert.DoesNotContain("href=\"/#contact\"", html);
    }

    [Fact]
    public async Task No_razor_source_leaks_into_the_rendered_page()
    {
        // Wrapping a block in a <div> puts Razor into markup mode, and a bare "if" after that
        // renders as literal text. It shipped that way: customers' contact sections showed
        // "if (ViewData["EnquirySent"] is true) {" above the form. Nothing in the suite noticed,
        // because every assertion was about content that was still present.
        var definition = SampleDefinition();
        definition.Sections.Add(new GallerySection
        {
            Heading = "Our work",
            Images = [new GalleryImage { Url = "https://example.test/a.jpg", AltText = "A job" }],
        });
        definition.Sections.Add(new AboutSection { Heading = "About us", Body = "A line." });
        definition.Sections.Add(new HoursMapSection { Heading = "Find us", AddressLines = ["12 High Street"] });
        definition.Sections.Add(new TestimonialsSection
        {
            Heading = "Reviews",
            Items = [new Testimonial { Quote = "Great", AuthorName = "Ama" }],
        });

        var (subdomain, _, _) = await SeedSiteAsync(definition: definition);

        var html = Decode(await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/"));

        // Fragments of C# that can only appear if a code block escaped into markup.
        foreach (var leak in new[] { "ViewData[", "@if", "@foreach", "string.IsNullOrWhiteSpace", "Model." })
        {
            Assert.DoesNotContain(leak, html);
        }
    }

    [Fact]
    public async Task The_enquiry_form_renders_its_fields_rather_than_its_source()
    {
        var (subdomain, _, _) = await SeedSiteAsync();

        var html = Decode(await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/"));

        Assert.Contains("name=\"message\"", html);
        Assert.Contains("Send message", html);
        Assert.DoesNotContain("EnquirySent", html);
    }

    [Fact]
    public async Task The_page_carries_a_nav_bar_a_footer_and_a_phone_call_bar()
    {
        var (subdomain, _, _) = await SeedSiteAsync();

        var html = Decode(await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/"));

        Assert.Contains("class=\"topbar\"", html);
        Assert.Contains("class=\"site-footer\"", html);
        // The one action most visitors to these sites come to take.
        Assert.Contains("class=\"callbar\"", html);
        Assert.Contains("tel:+233200000000", html);
    }

    [Fact]
    public async Task A_site_with_no_phone_or_whatsapp_gets_no_call_bar()
    {
        var definition = SampleDefinition();
        definition.Sections = [.. definition.Sections.Where(s => s is not ContactSection)];
        definition.Sections.Add(new ContactSection { Heading = "Get in touch", Email = "joe@example.com" });

        var (subdomain, _, _) = await SeedSiteAsync(definition: definition);

        var html = Decode(await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/"));

        Assert.DoesNotContain("class=\"callbar\"", html);
        // No bar means no space reserved for one at the bottom of the page.
        Assert.DoesNotContain("<body class=\"has-callbar\"", html);
    }

    [Fact]
    public async Task Fonts_are_declared_inline_and_served_from_our_own_origin()
    {
        // A third-party font request is a second DNS lookup and TLS handshake before the page can
        // be styled, on connections where that is the expensive part.
        var definition = SampleDefinition();
        definition.Theme.Fonts = new FontPair { Heading = "Fraunces", Body = "Inter" };

        var (subdomain, _, _) = await SeedSiteAsync(definition: definition);
        var client = CreateClient();

        var html = Decode(await client.GetStringAsync($"http://{subdomain}.platform.com/"));

        Assert.Contains("@font-face", html);
        Assert.Contains("/fonts/fraunces-latin-var.woff2", html);
        Assert.Contains("font-display: swap", html);
        Assert.DoesNotContain("fonts.googleapis.com", html);

        var font = await client.GetAsync($"http://{subdomain}.platform.com/fonts/inter-latin-var.woff2");
        Assert.Equal(HttpStatusCode.OK, font.StatusCode);
    }

    [Fact]
    public async Task A_theme_naming_a_font_we_do_not_host_downloads_nothing()
    {
        // Font names come out of jsonb written by an older build. An unknown one must degrade to a
        // system stack, never become a request for a file that does not exist.
        var definition = SampleDefinition();
        definition.Theme.Fonts = new FontPair { Heading = "Georgia", Body = "system-ui" };

        var (subdomain, _, _) = await SeedSiteAsync(definition: definition);

        var html = Decode(await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/"));

        Assert.DoesNotContain("@font-face", html);
        Assert.Contains("\"Georgia\", system-ui, sans-serif", html);
    }

    [Fact]
    public async Task The_font_stack_reaches_the_css_unescaped()
    {
        // Razor escapes by default, and CSS does not decode entities — a stack written as
        // &quot;Inter&quot; is thrown away by the parser and every site silently loses its type.
        var definition = SampleDefinition();
        definition.Theme.Fonts = new FontPair { Heading = "Fraunces", Body = "Inter" };

        var (subdomain, _, _) = await SeedSiteAsync(definition: definition);

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/");

        Assert.Contains("--font-heading: \"Fraunces\"", html);
        Assert.DoesNotContain("&quot;Fraunces&quot;", html);
    }

    [Fact]
    public async Task A_font_name_from_older_data_cannot_break_out_of_the_style_block()
    {
        // Written unescaped, so the sanitising in WebFontCatalog is the only thing standing between
        // a jsonb document and the page.
        var definition = SampleDefinition();
        definition.Theme.Fonts = new FontPair { Heading = "Bad\";}</style><script>alert(1)</script>", Body = "Inter" };

        var (subdomain, _, _) = await SeedSiteAsync(definition: definition);

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/");

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("</style>alert", html);
        // Only letters, digits, spaces and hyphens survive, so the declaration stays a declaration.
        Assert.Matches("--font-heading: \"[A-Za-z0-9 -]+\", system-ui, sans-serif;", html);
    }

    [Fact]
    public async Task A_generated_category_site_renders_its_stock_photography()
    {
        // The catalog is only worth having if the photographs survive generation, storage as jsonb,
        // publishing and the renderer. Everything else about categories is unit-tested; this is the
        // one check that a visitor actually sees them.
        var definition = await new TemplateSiteGenerator().GenerateAsync(new BusinessProfile
        {
            BusinessName = "Auntie Akos Kitchen",
            Category = "chop bar",
            Offerings = ["Jollof and chicken"],
            PhoneNumber = "+233200000000",
            AddressLines = ["12 High Street, Osu"],
        });

        var (subdomain, _, _) = await SeedSiteAsync(definition: definition);

        var html = Decode(await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/"));

        Assert.Contains("Our menu", html);
        Assert.Contains("From the kitchen", html);
        Assert.Contains("images.unsplash.com", html);
        Assert.Contains("alt=\"Three dishes laid out on a wooden table\"", html);
        // The renderer must not have rewritten a non-Cloudinary URL on the way through.
        Assert.Contains("w=1600&h=900", html);
    }

    [Fact]
    public async Task Non_ascii_text_is_emitted_as_utf8_rather_than_numeric_entities()
    {
        var definition = SampleDefinition();
        definition.Meta.SeoTitle = "Café Ámà — Accra";

        var (subdomain, _, _) = await SeedSiteAsync(definition: definition);

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/");

        Assert.Contains("Café Ámà — Accra", html);
        Assert.DoesNotContain("&#x2014;", html);
    }

    [Fact]
    public async Task The_theme_drives_the_rendered_styles()
    {
        var (subdomain, _, _) = await SeedSiteAsync();

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/");

        Assert.Contains("--primary: #0a7d55", html);
    }

    [Fact]
    public async Task Published_sites_carry_no_javascript()
    {
        var (subdomain, _, _) = await SeedSiteAsync();

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blazor", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hidden_sections_are_not_rendered()
    {
        var definition = SampleDefinition();
        definition.Sections[0].Visible = false;

        var (subdomain, _, _) = await SeedSiteAsync(definition: definition);

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/");

        Assert.DoesNotContain("Blocked drain?", html);
        Assert.Contains("Drain clearing", html);
    }

    [Fact]
    public async Task A_site_with_nothing_published_is_not_served()
    {
        var (subdomain, _, _) = await SeedSiteAsync(publish: false);

        var response = await CreateClient().GetAsync($"http://{subdomain}.platform.com/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("no website here yet", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Draft_edits_are_never_visible_to_visitors()
    {
        var (subdomain, siteId, tenantId) = await SeedSiteAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            // The tenant must be in scope before querying, or the filter hides the row.
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenantId;
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            var site = await db.Sites.FindAsync(siteId);

            ((HeroSection)site!.Draft.Sections[0]).Headline = "Unpublished draft headline";
            await db.SaveChangesAsync();
        }

        var html = await CreateClient().GetStringAsync($"http://{subdomain}.platform.com/");

        Assert.DoesNotContain("Unpublished draft headline", html);
        Assert.Contains("Blocked drain?", html);
    }

    [Fact]
    public async Task Unknown_paths_on_a_tenant_host_do_not_reach_builder_pages()
    {
        var (subdomain, _, _) = await SeedSiteAsync();

        var response = await CreateClient().GetAsync($"http://{subdomain}.platform.com/some-admin-page");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

[Collection(nameof(PostgresCollection))]
public class SiteOutputCacheTests(PostgresFixture fixture) : IDisposable
{
    private readonly TenantAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private async Task<(string Subdomain, Guid SiteId, Guid TenantId)> SeedPublishedSiteAsync(string headline)
    {
        var subdomain = $"c{Guid.NewGuid():N}"[..12];

        using var scope = _factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

        var tenant = new Tenant { Subdomain = subdomain, Name = "Cache Tenant" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        tenantContext.TenantId = tenant.Id;
        var site = new Site
        {
            Name = "Site",
            Draft = new SiteDefinition { Sections = [new HeroSection { Headline = headline }] },
        };
        site.Publish();
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        return (subdomain, site.Id, tenant.Id);
    }

    private async Task EditDraftAsync(Guid siteId, Guid tenantId, string headline)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenantId;
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

        var site = await db.Sites.FindAsync(siteId);
        ((HeroSection)site!.Draft.Sections[0]).Headline = headline;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_second_visit_is_served_from_the_cache()
    {
        var (subdomain, siteId, tenantId) = await SeedPublishedSiteAsync("First version");
        var client = _factory.CreateClient();
        var url = $"http://{subdomain}.platform.com/";

        Assert.Contains("First version", await client.GetStringAsync(url));

        // Publish a change behind the cache's back: the cached copy must still be served.
        await EditDraftAsync(siteId, tenantId, "Second version");
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenantId;
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            var site = await db.Sites.FindAsync(siteId);
            site!.Publish();
            await db.SaveChangesAsync();
        }

        Assert.Contains("First version", await client.GetStringAsync(url));
    }

    [Fact]
    public async Task Publishing_through_the_publisher_evicts_the_cached_site()
    {
        var (subdomain, siteId, tenantId) = await SeedPublishedSiteAsync("First version");
        var client = _factory.CreateClient();
        var url = $"http://{subdomain}.platform.com/";

        Assert.Contains("First version", await client.GetStringAsync(url));

        await EditDraftAsync(siteId, tenantId, "Second version");

        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenantId;
            await scope.ServiceProvider.GetRequiredService<SitePublisher>().PublishAsync(siteId);
        }

        Assert.Contains("Second version", await client.GetStringAsync(url));
    }

    [Fact]
    public async Task Publishing_one_tenant_does_not_evict_another()
    {
        var first = await SeedPublishedSiteAsync("Tenant one");
        var second = await SeedPublishedSiteAsync("Tenant two");
        var client = _factory.CreateClient();

        await client.GetStringAsync($"http://{first.Subdomain}.platform.com/");
        await client.GetStringAsync($"http://{second.Subdomain}.platform.com/");

        await EditDraftAsync(second.SiteId, second.TenantId, "Tenant two updated");
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = second.TenantId;
            await scope.ServiceProvider.GetRequiredService<SitePublisher>().PublishAsync(second.SiteId);
        }

        Assert.Contains("Tenant one", await client.GetStringAsync($"http://{first.Subdomain}.platform.com/"));
        Assert.Contains("Tenant two updated", await client.GetStringAsync($"http://{second.Subdomain}.platform.com/"));
    }

    [Fact]
    public async Task One_tenants_cached_page_is_never_served_to_another()
    {
        var first = await SeedPublishedSiteAsync("Tenant one");
        var second = await SeedPublishedSiteAsync("Tenant two");
        var client = _factory.CreateClient();

        await client.GetStringAsync($"http://{first.Subdomain}.platform.com/");
        var otherHtml = await client.GetStringAsync($"http://{second.Subdomain}.platform.com/");

        Assert.Contains("Tenant two", otherHtml);
        Assert.DoesNotContain("Tenant one", otherHtml);
    }

    [Fact]
    public async Task A_site_that_becomes_published_is_served_without_waiting_for_the_cache_to_expire()
    {
        // The 404 for "nothing published yet" must not be cached, or a newly published site
        // would stay invisible until the entry expired.
        var subdomain = $"c{Guid.NewGuid():N}"[..12];
        Guid tenantId, siteId;

        using (var scope = _factory.Services.CreateScope())
        {
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();
            var tenant = new Tenant { Subdomain = subdomain, Name = "Later Tenant" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            tenantId = tenant.Id;
            tenantContext.TenantId = tenantId;
            var site = new Site
            {
                Name = "Site",
                Draft = new SiteDefinition { Sections = [new HeroSection { Headline = "Now live" }] },
            };
            db.Sites.Add(site);
            await db.SaveChangesAsync();
            siteId = site.Id;
        }

        var client = _factory.CreateClient();
        var url = $"http://{subdomain}.platform.com/";

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenantId;
            await scope.ServiceProvider.GetRequiredService<SitePublisher>().PublishAsync(siteId);
        }

        Assert.Contains("Now live", await client.GetStringAsync(url));
    }
}
