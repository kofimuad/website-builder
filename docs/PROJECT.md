# CS Build — the whole project, A to Z

An AI-first website builder for small businesses with no technical knowledge. The owner answers an
interview, a site is generated for them, and they edit it by clicking the text they want to change.

Target market is West Africa, Ghana first. That single fact shapes more of this codebase than any
technical decision: WhatsApp is a first-class contact channel, phone numbers matter more than email,
pages must be light enough for a phone on mobile data, and payments — when they arrive — cannot be
Stripe.

- **Product name:** CS Build (was "Sitely"; dropped, a competitor holds it)
- **Domain:** `csbuild.app`, registered at Cloudflare
- **Hosting:** Railway, deployed from `main`
- **Jira:** project **WB** on `csharpworks.atlassian.net`
- **Repo:** `github.com/kofimuad/website-builder`

---

## 1. Where the project actually stands

**Live and working in production:**

- The marketing home page at `csbuild.app`
- Wildcard TLS across `*.csbuild.app` — tenant sites serve over HTTPS
- Tenant routing: apex serves the builder, subdomains resolve to tenant sites, unknown ones get a
  "no website here yet" page
- The full onboarding → generate → edit → publish loop
- Reserved subdomains protecting mail DNS and platform-lookalike names

**Built but switched off:**

| Feature | Why it's inert |
| --- | --- |
| Photo uploads (Cloudinary) | No Cloudinary account exists, so `Images:*` is unset. Onboarding and the editor both hide the upload UI |
| AI site generation (Gemini) | **No `GEMINI_API_KEY` yet.** Sites fall back to the deterministic template. Startup logs which is live |
| Sign-in email | Resend is DNS-verified, but whether the Railway variables are set is unconfirmed |

**Not built at all:** billing, admin tooling, SEO metadata, custom domains, rollback, analytics.

The onboarding **live preview** is the real generator run against the answers so far — the same
`TemplateSiteGenerator` that builds the site, projected into a small browser mock-up. **It never
calls the model**: it rebuilds on every keystroke, so it shows structure, imagery and layout rather
than final copy. A preview that reads generically is therefore not evidence the model is broken —
the startup banner and any `AI GENERATION FAILED` line in the log are. It was a
static picture of a plumbing site until 2026-08-08, which meant it showed a mechanic invented
guarantees and a headline about blocked drains.

**Tests:** 437 total, all passing, and the suite makes no model API calls at all — see §11.

---

## 2. Shape of the thing

One containerised ASP.NET Core app on .NET 9 serves both halves of the product:

- **Blazor Server** — the builder: onboarding, editor, dashboard, leads inbox
- **Razor Pages** — the published tenant sites: plain server-rendered HTML, no JS framework,
  output-cached

They share a process, a database, and a request pipeline. Which half you get is decided by the
`Host` header before routing runs.

```
                     ┌──────────────────────────────┐
  csbuild.app  ──────▶ TenantResolutionMiddleware   │
  *.csbuild.app ─────▶  classifies the Host header  │
                     └───────────┬──────────────────┘
                                 │
              ┌──────────────────┼─────────────────────┐
              ▼                  ▼                     ▼
        Platform            TenantSubdomain       CustomDomain
     (builder UI,          (rewrite → /site,       (not mapped
      Blazor Server)        Razor renderer)          → 404)
```

### Projects

| Project | Contains | Depends on |
| --- | --- | --- |
| `WebsiteBuilder.Core` | Domain model, site schema, generation, tenancy primitives | nothing |
| `WebsiteBuilder.Data` | EF Core `DbContext`, migrations, tenant query filters | Core |
| `WebsiteBuilder.Web` | Blazor UI, Razor renderer, auth, email, images, HTTP concerns | Core, Data |
| `WebsiteBuilder.Tests` | Unit and integration tests | all three |

Core has no framework dependencies beyond the BCL. That's deliberate: the site model, the
generation pipeline, and the tenancy rules are all testable without booting a web host.

---

## 3. Multi-tenancy

**One database, one schema, a `TenantId` column, and EF Core global query filters.** Not
schema-per-tenant, not database-per-tenant. For thousands of small sites, the operational cost of
either alternative dwarfs the isolation benefit.

`ITenantOwned` marks an entity as tenant-scoped. `WebsiteBuilderDbContext` applies a global filter
on every such entity against the ambient `TenantContext`, so a query that forgets to filter by
tenant returns nothing rather than everything.

`IgnoreQueryFilters()` appears in exactly two places, both deliberate and both commented: the
ownership gate in `SiteManagementService.LoadAsync`, and the cross-tenant dashboard listing.

### How a host becomes a tenant

`HostClassification.Classify` (Core) is pure and database-free. It returns one of three kinds:

- **Platform** — the apex, or a reserved subdomain. Serves the builder.
- **TenantSubdomain** — a single label. Looked up in `Tenants`; a miss renders "no website here yet".
- **CustomDomain** — anything else. 404 for now.

`TenantResolutionMiddleware` runs **before** `UseRouting()`. That ordering is load-bearing: with the
implicit routing middleware, the endpoint would already be selected before tenant resolution ran and
the not-found rewrite would be ignored.

On a tenant host, `/` is served (rewritten to `/site`), plus the shop allowlist and paths with file
extensions. Anything else 404s, so builder pages can never appear on a customer's domain. See §8a.

### Reserved subdomains

`TenantResolutionOptions.ReservedSubdomains` is a security control, not tidiness. Two reasons:

1. **A DNS wildcard only answers for names that have no records of their own.** The moment a label
   gets an MX or TXT record, `*.csbuild.app` stops covering it. Resend puts records on `send`, so a
   tenant given that name would resolve to Amazon's mail servers.
2. **Anyone can sign up.** A tenant at `login.csbuild.app` or `billing.csbuild.app` is a phishing
   address served under our certificate and our brand.

Both `HostClassification` and `SubdomainSuggester` read the same list. Configuration **appends** to
the defaults rather than replacing them — pinned by `TenantResolutionOptionsBindingTests`, because
relying on unverified binder behaviour for the list that keeps `send` out of tenant hands is not
good enough.

---

## 4. The site model

A site is two JSON snapshots on one row, both `jsonb`:

| Column | Meaning |
| --- | --- |
| `Draft` | What the owner is editing. Never served to visitors. |
| `Published` | What visitors see. `NULL` until first publish. |

Both use the same `SiteDefinition` shape, which is what lets the editor preview a draft through the
real renderer. `Site.Publish()` deep-copies draft over published so the live site never shares
objects with a draft still being edited.

```jsonc
{
  "schemaVersion": 1,
  "meta":    { "businessName": "...", "tagline": "...", "seoTitle": "...", "seoDescription": "..." },
  "theme":   { "palette": { "primary": "#1f5eff" }, "fonts": { "heading": "...", "body": "..." } },
  "sections": [ { "type": "hero", "id": "...", "visible": true, "headline": "..." } ]
}
```

Theme is separate from section content on purpose: restyling must never touch a word of copy, and
the generator writes the two independently.

**Section types** (the discriminator strings are persisted data — renaming one is a migration):
`hero`, `about`, `services`, `gallery`, `testimonials`, `contact`, `hoursMap`, `cta`, `shop`.

`SectionCatalog` drives the editor's picker. A new section type appears in the UI by adding one
entry there — no picker code changes.

### Schema versioning

`SiteDefinitionSerializer` is the only place definitions cross the JSON boundary. On read it upgrades
an older document to the current version before anything else sees it. Rules that keep this safe:

- Migration happens **on read**, never as a bulk `UPDATE`. Old and new documents coexist indefinitely.
- Never edit a published upgrade step; add a new one.
- Never renumber versions — they live in customer data.
- A document newer than the running build **throws**. After a rollback, an old build would otherwise
  quietly drop fields it doesn't understand and write the truncated result back.

Full detail: [site-schema.md](site-schema.md).

---

## 5. Generation

```
BusinessProfile → ISiteGenerator → SiteDefinition
                       │
       ┌───────────────┴──────────────┐
       ▼                              ▼
ModelSiteGenerator             TemplateSiteGenerator
 (needs API key)                 (always available)
       └──── FallbackSiteGenerator wraps both ────┘
```

`FallbackSiteGenerator` tries the model and falls back to the template on any failure that isn't
caller cancellation. **Onboarding must always end with a site**, even when the model is slow,
unavailable, or out of credit.

### The model provider

**Gemini, via `generateContent`.** `IModelJsonCompletion` is a one-method interface; the single
implementation is `GeminiJsonCompletion` in the web project, which is the only file that knows
which provider is in use. Nothing in Core names a vendor.

- **Plain `HttpClient`, no SDK.** One request, one JSON response, a stable documented wire format.
  A dependency on someone else's release cadence is a poor trade for the lines it would save, and
  the whole class is testable against a stub handler (`GeminiCompletionTests`).
- **The model id is configuration** (`Gemini:Model`, default `gemini-3.6-flash`). Google retires
  ids on a schedule — `gemini-2.0-flash` is already shut down — so moving to the next one is a
  Railway variable, not a deploy.
- **The schema crosses unchanged.** Gemini's `responseSchema` accepts the subset of OpenAPI 3.0
  that covers everything in `SiteGenerationSchema`, including `additionalProperties`. No
  translation layer, because a translation layer is one more thing that can be silently wrong.
- **Prices live with the provider.** `ModelCompletionResult` carries the cost the implementation
  worked out. When the previous provider's per-token prices were constants inside the generator,
  swapping providers would have kept reporting the old numbers.
- **Five JSON Schema keywords are stripped** before the schema is sent — `additionalProperties`,
  `$schema`, `$id`, `$defs`, `definitions`. `responseSchema` is an OpenAPI 3.0 subset and does not
  define them; sending one is a 400. None of them constrain what the model may return here, so
  dropping them costs nothing.
- **Errors are loud.** A non-2xx keeps Google's own message; `MAX_TOKENS` and `SAFETY` throw rather
  than returning half a site. `FallbackSiteGenerator` then logs at **Error** and builds the
  template site — because the visible symptom of a failed model call is nothing more than
  unexpectedly generic copy, which reads as "the AI isn't working" with nothing to point at.

Previously Anthropic Claude, removed in favour of Gemini; the implementation is one `git revert`
away if that decision is reversed.

### Category templates (WB-45)

**The model writes the words; the business category decides the page.** `CategoryTemplateCatalog`
holds seven templates — restaurant, salon, trades, consultant, church, events, and a general
fallback — and each one owns its section lineup, its per-section headings and its stock photography.
A restaurant leads with its menu and photographs of food under "Our menu" and "From the kitchen"; a
plumber leads with services and asks for a quote.

Both generators build their sections through `SitePlanBuilder`, so a page has the same shape
whichever one ran. They used to build their lineups separately, and the two drifted.

- **Matching is on the owner's own words.** They type "chop bar", not a dropdown choice.
  Keywords match at a word start; a trailing `*` makes one a stem (`plumb*` catches "plumbing").
  Without that rule `ngo` matches "mango". Where two categories match, the longer keyword wins, so
  the answer never depends on the order of the list. **An unmatched category is expected and
  fine** — `general` is the page every site got before this existed.
- **The stock photos are hotlinked from the Unsplash CDN**, sized per slot to match exactly what
  `_RenderedSite.cshtml` asks for (hero 1600×900, gallery 800×600, about 1200 wide). `auto=format`
  gets AVIF or WebP to browsers that take it — the hero is ~109 KB delivered rather than ~212 KB.
  They exist because at onboarding nobody has uploaded anything, and an empty gallery grid is worse
  than no gallery. When Cloudinary exists these can move there by changing `StockPhoto.Url`.
- **No portraits and no testimonials, deliberately.** A stranger's face on a one-person consultancy
  reads as "this is me", and a pre-filled quote is a fabricated review. Both are the invented facts
  that `GeneratedContentGuard` exists to stop, arriving by a different route.
- **Adding a category is one entry in the catalog.** Matching, section building, imagery and the
  onboarding suggestion chips are all generic over the list; `CategoryTemplateTests` builds every
  template in the catalog so an entry that needed code alongside it fails there rather than in
  production.

Supporting pieces: `SiteGenerationPrompt` (the prompt), `SiteGenerationSchema` (the JSON schema the
response is constrained to), `GeneratedContentGuard` (validation), `SiteContentAssembler` (turns
model output into a `SiteDefinition`), `ModelSectionAssistant` (the per-section "make this
friendlier" helper), `InMemoryAssistantRateLimiter` (usage gate).

Themes: `ThemePresetCatalog` holds curated palettes; `Wcag` checks contrast so a generated theme is
always legible.

### How a published site looks

Rebuilt 2026-08-09, warm and local. The page furniture is not optional decoration — each piece is
there for a reason:

- **A sticky call bar on phones.** Fixed to the bottom, `Call now` and `WhatsApp`, shown only when
  the profile has those numbers and hidden above 45rem. This audience calls and messages; making
  that one thumb press from anywhere is worth more than anything else on the page.
- **A top bar** naming sections by their own heading, so a restaurant's says "Our menu".
- **A hero that uses its photograph** as a full-bleed image with a gradient scrim and the text over
  it, rather than a picture dropped under the button. The scrim exists because the photo is one we
  have never seen, taken on a phone we do not control.
- **A footer**, cards with real borders and shadow, a coloured closing section, and a two-column
  about and contact once there is room.

**Fonts are self-hosted** — `WebFontCatalog`, two latin-subset variable files in `wwwroot/fonts`
(Fraunces for headings, Inter for body; both SIL OFL). Before this, every theme named a system font
like Georgia or Impact and every site rendered in the device default, which was most of why they
looked unfinished. A third-party font request would be a second DNS lookup and TLS handshake before
the page can be styled, which on these connections is the expensive part.

A theme naming a font we do not host degrades to a system stack and downloads nothing — font names
come out of jsonb an older build wrote, so they are treated as data. `StackFor` strips them to
letters, digits, spaces and hyphens because the stack is written into the `<style>` block
**unescaped**: CSS reads `&quot;` as four literal characters and discards the declaration.

---

## 6. Accounts and access

Passwordless. Two ways in:

**Magic links.** 32 random bytes, SHA-256 hashed at rest, single-use, 15-minute expiry, rate-limited
to 5 per address per 15 minutes. A rate-limited request still shows the success screen — saying "too
many attempts" would tell an attacker which addresses have accounts.

**Google OAuth.** Optional; the button only appears when both `Auth:GoogleClientId` and
`Auth:GoogleClientSecret` are set. Magic links carry the whole flow without it.

Cookies are `csbuild_auth` and `csbuild_external`, both from `Branding.CookiePrefix`.

**The ownership gate** is `SiteManagementService.LoadAsync`. A site is addressed by id, and loading it
puts that site's tenant into scope — so the check happens *before* scope is granted, not after.
Everything downstream trusts `TenantContext`, which makes that one method the gate protecting every
site in the database. A site that doesn't exist and a site that isn't yours are deliberately
indistinguishable.

The owner id is passed in rather than read from ambient state: inside a Blazor circuit there is no
`HttpContext`, and a gate that silently sees "no user" would fail open.

**Onboarding and Leads both render with `prerender: false`, and must stay that way.** Prerendering
runs `OnInitializedAsync` twice — once for the prerender pass, once when the circuit starts. Both
pages do something once-only in it: Leads marks leads read, and Onboarding redeems the stashed
interview. With prerendering on, the prerender pass built the site and the circuit pass then found
an empty stash and told the owner their answers had expired, on the very run that had just
succeeded. `The_onboarding_page_is_not_prerendered` guards it.

A claim link is also the kind of URL people revisit, so a claim that finds nothing now sends an
owner who already has a site to their dashboard rather than through the interview again.

Full detail: [accounts-and-access.md](accounts-and-access.md).

---

## 7. Publishing

`SitePublisher.PublishAsync` snapshots draft → published and evicts the output cache in the same
operation, so a stale page can never outlive a publish.

`TenantSiteCachePolicy` keys entries on **tenant id, not host** — a tenant reachable by more than one
host still shares one entry and one eviction. Entries live 5 minutes and are tagged so publishing
evicts exactly one site's pages.

**First publish asks for the address.** The dialog pre-fills the suggested subdomain, checks
availability with a 400 ms debounce, and validates through `SubdomainPolicy` — whose error messages
never use the words "subdomain" or "DNS", enforced by a test. After going live there's a confirmation
with a copyable link and a WhatsApp share; republishing stays quiet.

The address is **fixed once published**. Nothing redirects a link already printed on a card or sent
to a customer.

---

## 8. Leads

The whole point of the product. A visitor submits the contact form on a tenant site; the lead is
saved against the tenant and emailed to the owner. `EmailLeadNotifier` is scoped, not singleton — it
resolves the owner through the request's `DbContext`.

A mail failure never takes down the thing that triggered it: the lead is saved even if the
notification bounces.

---

## 8a. The shop

Ecommerce v1: **catalog, cart, and the order sent on WhatsApp.** No payment integration — these
sales already happen in chat, and a card form nobody in the market completes is worth less than a
message the owner can reply to. Paystack (Stripe-owned, works in Ghana, handles MoMo) can slot in
behind the same button later; Stripe itself cannot, see §12.

**Products are relational rows, not part of the jsonb definition.** A draft that got published
would overwrite whatever the catalog had become since; stock, when it arrives, is written by
customers buying rather than by the owner editing; and an order needs a foreign key to point at. So
a product is **live the moment it is saved** — there is no draft copy of a price, and the products
page in the builder says so. The definition carries only a `shop` section marking where the catalog
appears, which keeps the document a document.

The slug is unique **per tenant**, not globally: two businesses may both sell jollof. **It follows
the name.** Everything is added as "New item", so a slug that stayed put left the whole catalog
sitting at `/products/new-item-2`; the site's own links are always built from the current slug, so
the only cost is that a link shared before a rename stops working.

### What a tenant host serves

`TenantResolutionMiddleware` used to serve only `/`. It now serves an **allowlist** — `/shop`,
`/products/{slug}`, `/cart` — and nothing else. An allowlist rather than a blocklist, and it stays
one: a dashboard reachable at `joesplumbing.csbuild.app` is both a leak and a phishing surface.
Adding a public page means adding it there deliberately.

### The cart

A cookie, holding product ids and quantities and **nothing else**. It is unsigned on purpose,
because it carries no authority: every price, name and availability check is read from the database
at render time, so the worst a forged cookie can do is put a product into its own owner's basket.
Cookies are host-scoped by the browser, so one tenant's cart is unreadable on another's site.

A line whose product has been deleted or withdrawn is dropped and the cookie rewritten. Quantities
and line counts are clamped, so a hand-written cookie cannot make the cart page do unbounded work.

`OrderMessage` composes the WhatsApp text. It totals the order **only when every priced line shares
a currency** — a business selling in cedis and dollars gets no total rather than a wrong one, and
the owner confirms. Unpriced products are a real way to sell here and list as "ask for a price".

Shop pages are deliberately **not output-cached**: the cart is per-visitor and the catalog is live.
The home page still is, because it is the same for everybody.

## 9. Images

Cloudinary, with two decisions worth knowing:

**Resize on delivery, not on upload.** One original is stored and `ImageDelivery` rewrites the URL
per slot (`f_auto,q_auto` plus `c_limit` or `c_fill,g_auto`). This needed no schema change, and a URL
from anywhere else passes through untouched so sites built before uploads existed keep rendering.

**Signing is server-side.** An unsigned Cloudinary preset name is visible in page source and
effectively world-writable. `Images:ApiSecret` must never reach the browser.

**The multipart body has to be built by hand.** `StringContent` stamps
`Content-Type: text/plain` on every part, and Cloudinary reads a part with a content type as a
*file* — so `api_key`, `timestamp` and `signature` vanished and it rejected the upload as unsigned.
The field names also have to be quoted; .NET writes them bare. Both are pinned by
`CloudinaryUploadTests`, because the error Cloudinary returns for it — "Upload preset must be
specified when using unsigned upload" — describes a different mistake entirely.

Half-configured credentials refuse to start — the only symptom otherwise is an editor that never
shows the upload button, which is a long way from the cause.

**Photos are asked for during onboarding**, not only in the editor. The Photos step uploads through
the same `IImageStore` and stores the URLs on `BusinessProfile.PhotoUrls`. Two consequences in
`SitePlanBuilder`:

- The owner's photos become the gallery and the first becomes the hero. **The category's stock
  photography is the fallback, not the default** — which is what WB-45 asked for.
- A category whose lineup has no gallery (consultant) still gets one if photos were uploaded.
  Asking for photographs and then discarding them is the worse surprise.

Onboarding has no tenant yet — the tenant is created from the finished answers — so uploads land in
a folder keyed by the interview and stay there. Moving them afterwards would buy tidier folder
names at the price of rewriting URLs already saved in a site.

---

## 10. Email

**Resend over HTTPS, not SMTP.** Gmail was rejected as a provider (≈500/day cap, app passwords, no
DKIM alignment for our own domain).

This reverses the original decision, which was SMTP precisely so that changing provider stayed a
configuration change. The reasoning was sound; the hosting environment overruled it.
**Railway disables outbound SMTP on every plan below Pro**, and does it by dropping the packets
rather than refusing the connection — so `smtp.resend.com:587` produced
`SocketException (110): Connection timed out` four and a half minutes after each attempt, with the
owner looking at a confirmation screen and an empty inbox. Port 443 is never blocked. Changing the
plan would also have fixed it; paying monthly to keep an abstraction was the worse trade.

`SmtpEmailSender` is still here and still selected by `Email:SmtpHost`, so a host that permits SMTP
— or a provider with no HTTP API — remains a configuration change. `Email:ApiKey` wins when both
are set, because that is the state a Railway project is in while moving across.

Two fail-fast guards, both because the alternative symptom is *silence*:

- A provider configured with an empty `Email:FromAddress` throws at startup.
- With no SMTP host at all, `LogEmailSender` writes the message to the log instead. This is correct
  locally and catastrophic in production, so **startup now logs which mode is active.**

On the SMTP path only: `System.Net.Mail` can only do STARTTLS, so **port 465 will hang, not fail**.
Sends are bounded by `Email:SendTimeout` (20s) — `SmtpClient.Timeout` does not apply to the async
send, and a dropped connection otherwise hangs for minutes.

### When mail does not arrive

Three failures look identical to the person waiting for the email, and the UI cannot tell them
apart on purpose. In order of likelihood:

1. **No provider configured.** `LogEmailSender` "sends" successfully to the log, so the UI shows
   the confirmation screen. Outside Development startup now logs this at **Error**.
2. **Rate limited.** Five links per address per 15 minutes; the sixth silently shows the same
   confirmation, because saying "too many attempts" would confirm the address has an account. Easy
   to hit while testing repeatedly. Logged as a warning.
3. **The send actually failed.** Then the UI *does* say so ("We could not send the email just
   now"), and `SmtpEmailSender` logs the host, port, STARTTLS flag, from-address and SMTP status
   code — because the exception text alone rarely names whichever of those was wrong.

The startup banner reports which mode is live, so it is the first line to read.

---

## 11. Infrastructure

| Piece | Detail |
| --- | --- |
| Local database | `docker compose up -d` → Postgres 16 on port **55440** (not 5432, deliberately) |
| CI | GitHub Actions: build + test, then `railway up` on push to `main` |
| Container | `Dockerfile` at repo root |
| Migrations | Applied on boot — Railway has no separate release phase |
| DNS | Cloudflare, **all records DNS-only (grey cloud)** |
| TLS | Railway-issued; wildcard via a delegated `_acme-challenge` CNAME |

`.app` is on the HSTS preload list. There is no HTTP fallback — before a certificate issues the
domain is *unreachable*, not merely insecure.

### Configuration reference

| Variable | Notes |
| --- | --- |
| `DATABASE_URL` | Blank is treated as missing — an unresolved Railway reference arrives as `""` |
| `TenantResolution__PlatformDomain` | `csbuild.app`. Left at the `localhost` default, every real host 404s |
| `Platform__PublicBaseUrl` | `https://csbuild.app`. Links in email are built from it |
| `Email__ApiKey` | Resend API key (`re_…`). **The path that works on Railway** — SMTP is blocked below Pro |
| `Email__SmtpHost` / `SmtpPort` / `SmtpUser` / `SmtpPassword` | Only used when `Email__ApiKey` is unset. On Resend the user is literally `resend` |
| `Email__FromAddress` | **Required** once either is set |
| `Images__CloudName` / `ApiKey` / `ApiSecret` | All three or none |
| `Auth__GoogleClientId` / `GoogleClientSecret` | Optional pair |
| `Gemini__ApiKey` or `GEMINI_API_KEY` | Optional. Without it, the template generator runs |
| `Gemini__Model` | Defaults to `gemini-3.6-flash`. Set this when Google retires an id |

### Testing

xUnit. Integration tests run against **real Postgres** via Testcontainers, so Docker must be running.

**The suite costs nothing to run.** `TenantAppFactory` boots the host with the model API key blanked,
so onboarding tests use `TemplateSiteGenerator`. This is not cosmetic: the factory runs in the
Development environment, which loads the developer's user secrets, and the key lives there — so every
onboarding test used to be a live, billed Opus request. `TestHostGenerationTests` pins the guarantee,
because the failure mode is a bill rather than a red test.

The model path itself is covered by `SiteGenerationTests` and `SectionAssistantTests` using scripted
`IModelJsonCompletion` fakes, which pin its behaviour — including malformed and throwing responses —
more precisely than a live call could.

---

## 12. Decisions and why

- **jsonb over relational sections** — a site is a document, edited and published whole. Relational
  section tables would mean a join per section type and a migration per new one.
- **Draft/published as two snapshots of one shape** — the renderer doesn't care which it was handed,
  which is what makes preview-through-the-real-renderer possible.
- **Migrate on read, never bulk UPDATE** — a schema change can't take a maintenance window, and a
  rollback can't destroy data.
- **Delivery-time image resizing** — no schema change, and non-Cloudinary URLs keep working.
- **SMTP over a vendor SDK** — provider changes are configuration.
- **Half-configured providers refuse to start** — every one of these guards exists because the
  failure mode is silence, not an error.
- **Reserved subdomains err wide** — no small business wants `secure.` as its address.
- **The brand name lives in one file** (`Platform/Branding.cs`) — it has already changed once and
  the sweep touched nine files.
- **The domain is configuration, not branding** — it differs between environments; the name doesn't.

---

## 13. What is not built

| Epic | Status |
| --- | --- |
| WB-6 SEO | Nothing. No OG tags, canonical, `robots.txt`, `sitemap.xml`, favicon, or JSON-LD |
| WB-7 Billing | Nothing. **Specced around Stripe, which does not support Ghana-registered merchants** |
| WB-8 Admin & ops | Nothing. Abuse takedown must exist before public launch |
| WB-9 Domains | Publish is done; unpublish/rollback and custom domains are not |
| WB-3 | WB-45 category templates are built (§5); the on-a-phone design review is not done |

**Ecommerce v1 is built** — catalog, cart and order-on-WhatsApp. See §8a. Not built: payments,
stock levels, order records, delivery. An order exists only as a WhatsApp message; nothing about it
is stored, which is the honest description of what this version does.
