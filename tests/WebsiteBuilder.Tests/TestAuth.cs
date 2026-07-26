using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Data;

namespace WebsiteBuilder.Tests;

/// <summary>
/// Signs requests in as a given owner by reading a header, so tests can exercise authorised pages
/// without driving a real magic-link round trip. The redemption path itself is covered directly
/// against <c>OwnerSignInService</c>, where the rules actually live.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestAuth";
    public const string OwnerHeader = "X-Test-Owner";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Context.Request.Headers.TryGetValue(OwnerHeader, out var raw) || !Guid.TryParse(raw, out var ownerId))
        {
            // No header means anonymous, which is what the "must be signed in" tests rely on.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, ownerId.ToString()),
                new Claim(ClaimTypes.Email, $"{ownerId:N}@example.com"),
            ],
            SchemeName);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public static class TestAuthExtensions
{
    /// <summary>A client whose requests arrive signed in as <paramref name="ownerId"/>.</summary>
    public static HttpClient CreateClientAs<T>(
        this WebApplicationFactory<T> factory,
        Guid ownerId,
        bool allowAutoRedirect = true)
        where T : class
    {
        var client = factory.WithAuth().CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = allowAutoRedirect });

        client.DefaultRequestHeaders.Add(TestAuthHandler.OwnerHeader, ownerId.ToString());
        return client;
    }

    /// <summary>A client with the test scheme installed but nobody signed in.</summary>
    public static HttpClient CreateAnonymousClient<T>(
        this WebApplicationFactory<T> factory,
        bool allowAutoRedirect = false)
        where T : class
        => factory.WithAuth().CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = allowAutoRedirect });

    private static WebApplicationFactory<T> WithAuth<T>(this WebApplicationFactory<T> factory)
        where T : class
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(options =>
                {
                    // Authenticate from the test header, but leave the *challenge* on the cookie
                    // scheme so an anonymous request still redirects to /signin exactly as it
                    // does in production. Overriding both would turn every redirect into a 401
                    // and quietly stop testing the real behaviour.
                    options.DefaultScheme = TestAuthHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        }));

    /// <summary>Creates an owner row and returns its id. Owners sit outside the tenant filter.</summary>
    public static async Task<Guid> CreateOwnerAsync<T>(this WebApplicationFactory<T> factory, string? email = null)
        where T : class
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebsiteBuilderDbContext>();

        var owner = new Owner { Email = email ?? $"owner-{Guid.NewGuid():N}@example.com" };
        db.Owners.Add(owner);
        await db.SaveChangesAsync();

        return owner.Id;
    }
}
