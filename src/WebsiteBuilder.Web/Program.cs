using System.Text.Encodings.Web;
using System.Text.Unicode;
using Anthropic;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Core.Generation;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;
using WebsiteBuilder.Web.Auth;
using WebsiteBuilder.Web.Caching;
using WebsiteBuilder.Web.Components;
using WebsiteBuilder.Web.Development;
using WebsiteBuilder.Web.Email;
using WebsiteBuilder.Web.Generation;
using WebsiteBuilder.Web.Management;
using WebsiteBuilder.Web.Middleware;
using WebsiteBuilder.Web.Onboarding;
using WebsiteBuilder.Web.Platform;
using WebsiteBuilder.Web.Publishing;

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

// No SMTP host configured means a developer can still sign in: the link goes to the log.
var emailOptions = builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
if (emailOptions.IsConfigured)
{
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
}
else
{
    builder.Services.AddScoped<IEmailSender, LogEmailSender>();
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
        options.Cookie.Name = "sitely_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

// Google is optional in exactly the way Claude is above: configure it and the button appears,
// leave it out and magic-link sign-in carries the whole flow.
if (authOptions.IsGoogleConfigured)
{
    authentication
        .AddCookie(AuthSchemes.External, options =>
        {
            options.Cookie.Name = "sitely_external";
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

// Site generation. The deterministic template always exists; when an Anthropic API key is
// configured, Claude writes the copy and the template becomes the fallback for when the model
// fails or is unavailable.
builder.Services.AddSingleton<TemplateSiteGenerator>();

var anthropicKey = builder.Configuration["ANTHROPIC_API_KEY"]
    ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

if (string.IsNullOrWhiteSpace(anthropicKey))
{
    builder.Services.AddSingleton<ISiteGenerator>(sp => sp.GetRequiredService<TemplateSiteGenerator>());
}
else
{
    builder.Services.AddSingleton(new AnthropicClient { ApiKey = anthropicKey });
    builder.Services.AddSingleton<IClaudeJsonCompletion, AnthropicClaudeCompletion>();
    builder.Services.AddSingleton<ClaudeSiteGenerator>();
    builder.Services.AddSingleton<ISiteGenerator>(sp => new FallbackSiteGenerator(
        primary: sp.GetRequiredService<ClaudeSiteGenerator>(),
        fallback: sp.GetRequiredService<TemplateSiteGenerator>(),
        logger: sp.GetRequiredService<ILogger<FallbackSiteGenerator>>()));

    // The per-section assistant needs the model, so it exists only when the key does.
    builder.Services.AddSingleton<ISectionAssistant, ClaudeSectionAssistant>();
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
