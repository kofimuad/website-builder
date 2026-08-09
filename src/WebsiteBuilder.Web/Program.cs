using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Anthropic;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebsiteBuilder.Core.Generation;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Auth;
using WebsiteBuilder.Web.Caching;
using WebsiteBuilder.Web.Components;
using WebsiteBuilder.Web.Development;
using WebsiteBuilder.Web.Email;
using WebsiteBuilder.Web.Generation;
using WebsiteBuilder.Web.Images;
using WebsiteBuilder.Web.Management;
using WebsiteBuilder.Web.Middleware;
using WebsiteBuilder.Web.Onboarding;
using WebsiteBuilder.Web.Platform;
using WebsiteBuilder.Web.Publishing;
using WebsiteBuilder.Web.Shop;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddRazorPages();
builder.Services.AddHealthChecks();

// Blank is treated as missing, not as a value: a Railway reference variable whose target does
// not exist resolves to an empty string, and passing that on fails far from the real cause.
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = builder.Configuration["DATABASE_URL"];
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "No database connection string. Set ConnectionStrings:Default or DATABASE_URL. " +
        "If DATABASE_URL is set but empty, a variable reference such as ${{Postgres.DATABASE_URL}} " +
        "did not resolve — check that the referenced service name matches exactly.");
}

builder.Services.AddWebsiteBuilderData(connectionString);
builder.Services.Configure<TenantResolutionOptions>(
    builder.Configuration.GetSection(TenantResolutionOptions.SectionName));
builder.Services.AddScoped<SitePublisher>();
builder.Services.AddScoped<OnboardingService>();
builder.Services.AddScoped<SiteManagementService>();
builder.Services.AddScoped<WebsiteBuilder.Web.Leads.LeadsService>();
// Scoped, not singleton: it resolves the owner to notify through the request's DbContext.
builder.Services.AddScoped<WebsiteBuilder.Web.Leads.ILeadNotifier, WebsiteBuilder.Web.Leads.EmailLeadNotifier>();

// Sign-in (WB-15) and the transactional email both routes depend on.
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<PlatformOptions>(builder.Configuration.GetSection(PlatformOptions.SectionName));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddScoped<PlatformUrls>();
builder.Services.AddScoped<OwnerSignInService>();
builder.Services.AddScoped<OnboardingDraftStore>();

// No provider configured means a developer can still sign in: the link goes to the log.
var emailOptions = builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();

if (emailOptions.IsConfigured)
{
    // A provider with no From address sends nothing anybody receives, and the symptom is silence:
    // sign-in links that were "sent" and never arrive. Refuse to start instead.
    if (string.IsNullOrWhiteSpace(emailOptions.FromAddress))
    {
        throw new InvalidOperationException(
            "An email provider is configured but Email:FromAddress is empty. Set it to an address " +
            "on a domain the provider has verified for us, e.g. no-reply@ourdomain.com — mail from " +
            "an unverified domain bounces.");
    }
}

if (emailOptions.UsesApi)
{
    // HTTPS, because outbound SMTP is blocked on Railway below the Pro plan. See ResendEmailSender.
    builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
    {
        client.BaseAddress = new Uri("https://api.resend.com/");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", emailOptions.ApiKey);
        client.Timeout = emailOptions.SendTimeout;
    });
}
else if (emailOptions.UsesSmtp)
{
    // SmtpEmailSender uses System.Net.Mail, which can only start TLS on a plaintext connection.
    // It cannot speak implicit TLS, which is the whole point of 465 — so a send there does not
    // fail cleanly, it hangs until the timeout and looks like the provider ignoring us.
    if (emailOptions.SmtpPort == 465)
    {
        throw new InvalidOperationException(
            "Email:SmtpPort is 465, which this app cannot use: it sends with System.Net.Mail, and " +
            "that only supports STARTTLS, not implicit TLS. Use 587, or set Email:ApiKey to send " +
            "over HTTPS instead.");
    }

    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
}
else
{
    builder.Services.AddScoped<IEmailSender, LogEmailSender>();
}

// The shop (WB ecommerce v1). Products are relational and live, so they are read per request
// under the resolved tenant rather than snapshotted into the published document.
builder.Services.AddScoped<ShopCatalog>();
builder.Services.AddScoped<ProductsService>();

// Photo uploads (WB-23). Cloudinary keeps the original and resizes on delivery, so the editor
// stores a URL and each slot asks for the size it needs. Without credentials the editor offers no
// uploads at all, the same way the assistant does not exist without a model key.
builder.Services.Configure<ImageOptions>(builder.Configuration.GetSection(ImageOptions.SectionName));
var imageOptions = builder.Configuration.GetSection(ImageOptions.SectionName).Get<ImageOptions>() ?? new ImageOptions();

// Half-configured is the dangerous state: uploads would be silently absent, and the only symptom
// is an editor that never shows the button. Refusing to start beats hunting for that later.
if (imageOptions.IsPartiallyConfigured)
{
    throw new InvalidOperationException(
        "Images: CloudName, ApiKey and ApiSecret must all be set, or all be left unset. Set them " +
        "with: dotnet user-secrets set \"Images:ApiSecret\" \"…\" --project src/WebsiteBuilder.Web");
}

if (imageOptions.IsConfigured)
{
    builder.Services.AddHttpClient<IImageStore, CloudinaryImageStore>(client =>
        // A 12 MB photo over a phone connection is slow but not broken; the default 100 s would
        // give up on uploads that were going to succeed.
        client.Timeout = TimeSpan.FromMinutes(3));
}

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

var authentication = builder.Services
    .AddAuthentication(AuthSchemes.Application)
    .AddCookie(AuthSchemes.Application, options =>
    {
        options.LoginPath = AuthEndpoints.SignInPath;
        options.AccessDeniedPath = AuthEndpoints.SignInPath;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Cookie.Name = $"{Branding.CookiePrefix}_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

// Google sign-in is optional in exactly the way the model is below: configure it and the button appears,
// leave it out and magic-link sign-in carries the whole flow.
if (authOptions.IsGoogleConfigured)
{
    authentication
        .AddCookie(AuthSchemes.External, options =>
        {
            options.Cookie.Name = $"{Branding.CookiePrefix}_external";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            options.Cookie.SameSite = SameSiteMode.Lax;
        })
        .AddGoogle(options =>
        {
            options.ClientId = authOptions.GoogleClientId!;
            options.ClientSecret = authOptions.GoogleClientSecret!;
            options.SignInScheme = AuthSchemes.External;
            options.CallbackPath = "/auth/google-signin";
            options.SaveTokens = false;
        });
}

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Site generation. The deterministic template always exists; when a model provider is configured,
// the model writes the copy and the template becomes the fallback for when it fails or is
// unavailable.
builder.Services.AddSingleton<TemplateSiteGenerator>();

builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));

// Both providers accept two names for their key: the one the rest of this app's configuration
// would suggest, and the one every vendor example uses — which is the one someone will reach for
// on Railway. Accept either rather than make anyone guess, preferring the explicit section.
var anthropicKey = builder.Configuration[$"{AnthropicOptions.SectionName}:ApiKey"]
    ?? builder.Configuration["ANTHROPIC_API_KEY"]
    ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

var geminiKey = builder.Configuration[$"{GeminiOptions.SectionName}:ApiKey"]
    ?? builder.Configuration["GEMINI_API_KEY"]
    ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

// Anthropic wins when both are set. Two providers can be configured at once — a key left behind
// on Railway is the normal case, not a mistake — so the choice has to be a stated rule rather than
// registration order, and the startup banner below says which one won.
var hasAnthropic = !string.IsNullOrWhiteSpace(anthropicKey);
var hasGemini = !string.IsNullOrWhiteSpace(geminiKey);

if (hasAnthropic)
{
    builder.Services.Configure<AnthropicOptions>(o => o.ApiKey = anthropicKey!);

    builder.Services.AddSingleton(sp =>
    {
        var anthropic = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;

        return new AnthropicClient
        {
            ApiKey = anthropic.ApiKey,
            Timeout = TimeSpan.FromSeconds(anthropic.TimeoutSeconds),
        };
    });

    builder.Services.AddSingleton<IModelJsonCompletion, AnthropicJsonCompletion>();
}
else if (hasGemini)
{
    builder.Services.Configure<GeminiOptions>(o => o.ApiKey = geminiKey!);

    builder.Services.AddHttpClient<IModelJsonCompletion, GeminiJsonCompletion>((sp, client) =>
    {
        var gemini = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;

        client.BaseAddress = new Uri(gemini.BaseUrl);
        // The header form keeps the key out of request URLs, which is where it would otherwise end
        // up in every proxy and access log between here and Google.
        client.DefaultRequestHeaders.Add("x-goog-api-key", gemini.ApiKey);
        // Generation is a foreground step in onboarding with a person watching a spinner. The
        // default 100 seconds is far longer than anyone will wait.
        client.Timeout = TimeSpan.FromSeconds(60);
    });
}

if (hasAnthropic || hasGemini)
{
    builder.Services.AddSingleton<ModelSiteGenerator>();
    builder.Services.AddSingleton<ISiteGenerator>(sp => new FallbackSiteGenerator(
        primary: sp.GetRequiredService<ModelSiteGenerator>(),
        fallback: sp.GetRequiredService<TemplateSiteGenerator>(),
        logger: sp.GetRequiredService<ILogger<FallbackSiteGenerator>>()));

    // The per-section assistant needs the model, so it exists only when a key does.
    builder.Services.AddSingleton<ISectionAssistant, ModelSectionAssistant>();
}
else
{
    builder.Services.AddSingleton<ISiteGenerator>(sp => sp.GetRequiredService<TemplateSiteGenerator>());
}

// Usage gate for the assistant; harmless when the assistant isn't available.
builder.Services.Configure<AssistantOptions>(builder.Configuration.GetSection(AssistantOptions.SectionName));
builder.Services.AddSingleton<IAssistantRateLimiter, InMemoryAssistantRateLimiter>();

// Emit non-ASCII text as UTF-8 rather than numeric entities. Business names and copy are often
// accented or non-Latin, and escaping every such character inflates the page for no benefit.
builder.Services.AddWebEncoders(options =>
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.AddOutputCache(options =>
    options.AddPolicy(TenantSiteCachePolicy.Name, new TenantSiteCachePolicy()));

var app = builder.Build();

// Which generator is live is otherwise invisible until someone notices the copy reads generically.
var tenantOptions = builder.Configuration.GetSection(TenantResolutionOptions.SectionName).Get<TenantResolutionOptions>()
    ?? new TenantResolutionOptions();

app.Logger.LogInformation(
    "Tenant subdomains hang off {PlatformDomain}. Sign-in email: {Mail}. Site generation: {Generator}. " +
    "Per-section assistant: {Assistant}. Photo uploads: {Images}.",
    tenantOptions.PlatformDomain,
    emailOptions.Describe(),
    hasAnthropic
        ? $"Anthropic ({builder.Configuration[$"{AnthropicOptions.SectionName}:Model"] ?? new AnthropicOptions().Model}), template fallback"
        : hasGemini
            ? $"Gemini ({builder.Configuration[$"{GeminiOptions.SectionName}:Model"] ?? new GeminiOptions().Model}), template fallback"
            : "template only — no Anthropic or Gemini key configured",
    hasAnthropic || hasGemini ? "available" : "unavailable",
    imageOptions.IsConfigured ? $"Cloudinary ({imageOptions.CloudName})" : "unavailable");

// A leftover key is not an error, but silently ignoring a provider someone believes is live is the
// kind of thing that gets debugged for an hour.
if (hasAnthropic && hasGemini)
{
    app.Logger.LogWarning(
        "Both an Anthropic and a Gemini key are configured. Anthropic wins; the Gemini key is " +
        "unused. Remove one to make the choice obvious from the variables alone.");
}

// Deployed with the default, every real host classifies as an unmapped custom domain and the whole
// platform answers 404 — including the marketing page. Warned rather than thrown because the test
// host boots in Production without a domain and has no tenants to resolve.
if (!app.Environment.IsDevelopment() && tenantOptions.PlatformDomain is "localhost")
{
    app.Logger.LogWarning(
        "TenantResolution:PlatformDomain is still \"localhost\" outside Development. Every request " +
        "to a real host name will 404 as an unmapped custom domain. Set it to the domain tenant " +
        "subdomains hang off, e.g. TenantResolution__PlatformDomain=csbuild.app.");
}

// Outside Development, no SMTP host means nobody can sign in and no owner hears about a lead —
// and the visible symptom is a confirmation screen followed by an email that never arrives. It is
// not fatal (tenant sites keep serving), so this shouts rather than throws.
if (!app.Environment.IsDevelopment() && !emailOptions.IsConfigured)
{
    app.Logger.LogError(
        "NO EMAIL PROVIDER CONFIGURED. Sign-in links and lead notifications are being written to " +
        "this log instead of being sent. Set Email__ApiKey to a Resend API key and " +
        "Email__FromAddress to an address on a domain Resend has verified.");
}

// Railway has no separate release phase, so pending migrations are applied on boot.
if (app.Configuration.GetValue("RunMigrationsOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>().Database.MigrateAsync();
}

if (app.Environment.IsDevelopment() && app.Configuration.GetValue("SeedDemoData", false))
{
    await DemoDataSeeder.SeedAsync(app.Services);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Routing is placed explicitly: with the implicit UseRouting the endpoint would already be
// selected before tenant resolution ran, and its not-found rewrite would be ignored.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseRouting();

// After tenant resolution so the cache key can include the tenant.
app.UseOutputCache();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapRazorPages();
app.MapAuthEndpoints();
app.MapHealthChecks("/healthz");

app.Run();

/// <summary>Exposed so integration tests can boot the real pipeline via WebApplicationFactory.</summary>
public partial class Program;
