using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebsiteBuilder.Core.Generation;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Web.Onboarding;

namespace WebsiteBuilder.Web.Pages;

/// <summary>
/// Renders the in-progress interview through the real site renderer, for the iframe in the
/// onboarding wizard.
///
/// <para>
/// <b>Anonymous on purpose.</b> Onboarding happens before sign-in — that is the product — so
/// there is no identity to authorise against. What guards the content is the token: 16 random
/// bytes, minted per circuit, never in a link, and holding nothing but the answers the visitor is
/// currently typing into their own screen.
/// </para>
///
/// <para>
/// The definition is generated here rather than stored, so the store stays a bag of answers and
/// this page stays the only thing that knows how answers become a site. It runs the deterministic
/// <see cref="TemplateSiteGenerator"/>: pure string work, no I/O, and safe to run on every request.
/// The model never runs here — it writes better words, but it costs money and takes about a minute,
/// which is neither free nor instant enough for something that redraws while you type.
/// </para>
///
/// <para>
/// Products are always empty. A shop section cannot exist during onboarding: the catalog is
/// relational and belongs to a tenant that has not been created yet.
/// </para>
/// </summary>
[AllowAnonymous]
public class OnboardingPreviewModel(OnboardingPreviewStore store) : PageModel
{
    private static readonly TemplateSiteGenerator Generator = new();

    /// <summary>Null when the entry has expired, which the view renders as a plain message.</summary>
    public SiteDefinition? Definition { get; private set; }

    public string Token { get; private set; } = "";

    public void OnGet(string token)
    {
        Token = token;

        var answers = store.Get(token);
        if (answers is not null)
        {
            Definition = Generator.Generate(answers);
        }

        // No-store: this URL is stable across the whole interview and only its content changes, so
        // a cached copy would freeze the preview on the first thing the visitor typed.
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    }
}
