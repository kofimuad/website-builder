using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using WebsiteBuilder.Core.Entities;

namespace WebsiteBuilder.Web.Auth;

public static class AuthSchemes
{
    /// <summary>The signed-in owner's session cookie.</summary>
    public const string Application = CookieAuthenticationDefaults.AuthenticationScheme;

    /// <summary>
    /// Short-lived cookie that only carries Google's reply from the callback into our own handler.
    /// Google's claims are never the session: the session is our owner id, so a Google account
    /// that later changes address still maps to the same owner.
    /// </summary>
    public const string External = "External";
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in owner's id, or null when anonymous or the claim is unreadable.</summary>
    public static Guid? OwnerId(this ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public static string? OwnerEmail(this ClaimsPrincipal? principal) => principal?.FindFirstValue(ClaimTypes.Email);

    public static string OwnerDisplayName(this ClaimsPrincipal? principal)
    {
        var name = principal?.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(name) ? principal.OwnerEmail() ?? "" : name;
    }

    /// <summary>
    /// The signed-in owner behind a component's cascading authentication state. This is how pages
    /// read identity: inside an interactive circuit there is no HttpContext, so the cascaded state
    /// is the only source that stays correct after the initial render.
    /// </summary>
    public static async Task<Guid?> OwnerIdAsync(
        this Task<Microsoft.AspNetCore.Components.Authorization.AuthenticationState>? state)
    {
        if (state is null)
        {
            return null;
        }

        return (await state).User.OwnerId();
    }
}

public static class AuthEndpoints
{
    public const string SignInPath = "/signin";

    /// <summary>
    /// Sign-in lives in endpoints rather than in components because writing an auth cookie needs a
    /// live response. An interactive Blazor circuit has none — its HTTP response finished when the
    /// page loaded — so <c>SignInAsync</c> from a component would silently do nothing.
    /// </summary>
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapGet("/google", (string? returnUrl, HttpContext context, IServiceProvider services) =>
        {
            if (!IsGoogleEnabled(services))
            {
                return Results.Redirect(SignInPath);
            }

            var safeReturn = OwnerSignInService.SafeReturnUrl(returnUrl);
            var callback = "/auth/google-callback"
                + (safeReturn is null ? "" : $"?returnUrl={Uri.EscapeDataString(safeReturn)}");

            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = callback },
                [GoogleDefaults.AuthenticationScheme]);
        });

        group.MapGet("/google-callback", async (
            string? returnUrl,
            HttpContext context,
            OwnerSignInService signIn,
            ILoggerFactory loggerFactory) =>
        {
            var result = await context.AuthenticateAsync(AuthSchemes.External);

            if (!result.Succeeded || result.Principal is null)
            {
                loggerFactory.CreateLogger(nameof(AuthEndpoints))
                    .LogWarning("Google callback did not carry a usable principal: {Failure}", result.Failure?.Message);
                return Results.Redirect($"{SignInPath}?error=google");
            }

            var email = result.Principal.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrWhiteSpace(email))
            {
                // Without an address we cannot tie this to an owner, and the whole model is
                // "email is the identity". Better to say so than to invent an account.
                return Results.Redirect($"{SignInPath}?error=noemail");
            }

            var owner = await signIn.FromGoogleAsync(
                email,
                result.Principal.FindFirstValue(ClaimTypes.Name),
                result.Principal.FindFirstValue(ClaimTypes.NameIdentifier),
                context.RequestAborted);

            await context.SignOutAsync(AuthSchemes.External);
            await context.SignInAsync(AuthSchemes.Application, Build(owner), Persistent());

            return Results.Redirect(OwnerSignInService.SafeReturnUrl(returnUrl) ?? "/dashboard");
        });

        group.MapGet("/verify", async (string? token, HttpContext context, OwnerSignInService signIn) =>
        {
            var redeemed = await signIn.RedeemAsync(token, context.RequestAborted);

            if (redeemed is null)
            {
                return Results.Redirect($"{SignInPath}?error=link");
            }

            await context.SignInAsync(AuthSchemes.Application, Build(redeemed.Value.Owner), Persistent());

            return Results.Redirect(redeemed.Value.ReturnUrl ?? "/dashboard");
        });

        // Antiforgery is off here because the button lives in interactive components, which cannot
        // render a server antiforgery token. The exposure is a forced sign-out — an annoyance that
        // grants an attacker nothing — and the endpoint reads no input.
        group.MapPost("/signout", async (HttpContext context) =>
        {
            await context.SignOutAsync(AuthSchemes.Application);
            return Results.Redirect("/");
        }).DisableAntiforgery();
    }

    private static bool IsGoogleEnabled(IServiceProvider services) =>
        services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>().Value.IsGoogleConfigured;

    private static ClaimsPrincipal Build(Owner owner)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, owner.Id.ToString()),
                new Claim(ClaimTypes.Email, owner.Email),
                new Claim(ClaimTypes.Name, string.IsNullOrWhiteSpace(owner.Name) ? owner.Email : owner.Name),
            ],
            authenticationType: AuthSchemes.Application);

        return new ClaimsPrincipal(identity);
    }

    // Owners are small-business people on their own devices who check in every few weeks; a session
    // cookie would sign them out constantly for no security gain.
    private static AuthenticationProperties Persistent() => new()
    {
        IsPersistent = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
    };
}
