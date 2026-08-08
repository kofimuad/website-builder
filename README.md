# Website Builder

An AI-first website builder for small businesses with no technical knowledge. Owners answer an
interview, get a site generated for them, and edit it by clicking on the text they want to change.

Jira project: **WB** on csharpworks.atlassian.net.

## Running locally

You need the .NET 9 SDK and Docker.

```bash
docker compose up -d                        # Postgres on port 55440
dotnet run --project src/WebsiteBuilder.Web
```

The app applies its migrations on startup, and in Development it seeds one published demo site.

| URL                                    | What you get                                  |
| -------------------------------------- | --------------------------------------------- |
| <http://localhost:5184>                | The builder app (still the default template)   |
| <http://localhost:5184/signin>         | Sign in                                        |
| <http://localhost:5184/dashboard>      | The owner's dashboard (sign-in required)       |
| <http://joesplumbing.localhost:5184>   | A published demo tenant site                   |
| <http://nosuchbusiness.localhost:5184> | The "no website here yet" page                 |
| <http://localhost:5184/healthz>        | Health check                                   |

### Signing in locally

With no SMTP host configured, sign-in email is written to the console instead of sent. Enter any
address at `/signin`, then copy the `http://localhost:5184/auth/verify?token=…` line out of the log
and open it. The link works once and expires after 15 minutes.

Google sign-in is optional and hidden unless `Auth:GoogleClientId` and `Auth:GoogleClientSecret`
are set — magic links carry the whole flow without it. If you do configure Google, its authorised
redirect URI is `/auth/google-signin`.

The demo seeder creates an owner for the demo site so it is reachable from the dashboard; sign in
as `demo@joesplumbing.example` to see it. Tenants created before sign-in existed have no owner and
cannot be managed by anyone — their published sites still serve normally.

### Local secrets

`appsettings.Development.json` is committed, so keys must not go in it. Use user secrets, which are
stored per-machine outside the repo and loaded automatically in Development:

```bash
dotnet user-secrets set "ANTHROPIC_API_KEY" "sk-ant-…"        --project src/WebsiteBuilder.Web
dotnet user-secrets set "Auth:GoogleClientId" "…"             --project src/WebsiteBuilder.Web
dotnet user-secrets set "Auth:GoogleClientSecret" "…"         --project src/WebsiteBuilder.Web
dotnet user-secrets set "Email:SmtpHost" "smtp.resend.com"    --project src/WebsiteBuilder.Web
dotnet user-secrets set "Images:CloudName" "…"                --project src/WebsiteBuilder.Web
dotnet user-secrets set "Images:ApiKey" "…"                   --project src/WebsiteBuilder.Web
dotnet user-secrets set "Images:ApiSecret" "…"                --project src/WebsiteBuilder.Web
dotnet user-secrets list --project src/WebsiteBuilder.Web     # check what is set
```

### Photos

Photo uploads go to Cloudinary. `Images:CloudName`, `Images:ApiKey` and `Images:ApiSecret` must
all be set or all be left unset — half-configured refuses to start, because the only symptom would
be an editor that never shows the upload button.

Without them everything still works: the editor simply does not offer uploads, exactly as the
per-section assistant does not exist without a model key.

Uploads are signed server-side rather than using an unsigned preset, so size and type limits are
enforced here before anything reaches the provider. Images are **resized on delivery**, not on
upload — the original is stored once and `ImageDelivery` rewrites the URL for the size each slot
needs. A URL from anywhere else is passed through untouched, so sites built before uploads existed
keep rendering.

With `ANTHROPIC_API_KEY` set, Claude writes the site copy and the deterministic template becomes
the fallback for when the model fails; the per-section AI assistant in the editor also appears,
since it only exists when the model does. Without the key everything still works — sites are built
from the template and the assistant is hidden.

Chrome, Edge and Firefox resolve any `*.localhost` name to 127.0.0.1 on their own. If your browser
does not, add `127.0.0.1 joesplumbing.localhost` to `C:\Windows\System32\drivers\etc\hosts`.

Set `SeedDemoData` to `false` in `appsettings.Development.json` to start from an empty database.

## Tests

```bash
dotnet test
```

Integration tests start a real Postgres in Docker via Testcontainers, so Docker must be running.

## Layout

| Project                    | Contains                                                        |
| -------------------------- | --------------------------------------------------------------- |
| `WebsiteBuilder.Web`       | Blazor Server builder UI, Razor renderer for published sites     |
| `WebsiteBuilder.Core`      | Domain model, site definition schema, tenancy primitives         |
| `WebsiteBuilder.Data`      | EF Core DbContext, migrations, tenant query filters              |
| `WebsiteBuilder.Tests`     | Unit and integration tests                                       |

How sites are stored, and how to change that shape safely, is in [docs/site-schema.md](docs/site-schema.md).
Who is allowed to edit what, and where the checks live, is in
[docs/accounts-and-access.md](docs/accounts-and-access.md).

## Deployment

Deployed on Railway from `main`. The service needs:

| Variable                             | Notes                                                        |
| ------------------------------------ | ------------------------------------------------------------ |
| `DATABASE_URL`                       | Reference the Postgres service, e.g. `${{Postgres.DATABASE_URL}}` |
| `TenantResolution__PlatformDomain`   | The domain tenant subdomains hang off — `csbuild.app`. Defaults to `localhost`, and left at the default every real host 404s |
| `Platform__PublicBaseUrl`            | Absolute URL of the builder, `https://csbuild.app`. Links in email are built from it |
| `Email__SmtpHost`                    | Leave unset and email is logged, not sent — sign-in becomes unusable in production |
| `Email__SmtpPort`                    | Defaults to 587                                               |
| `Email__SmtpUser` / `Email__SmtpPassword` | Provider credentials. On Resend the user is literally `resend` and the password is the API key |
| `Email__FromAddress`                 | **Required** once `SmtpHost` is set — startup fails without it. Must be on a domain the provider has verified |
| `Email__FromName`                    | Optional display name. Blank sends the address alone           |
| `Images__CloudName`                  | Cloudinary account. All three image variables must be set together, or none |
| `Images__ApiKey`                     | Optional as a group. Without them the editor offers no photo uploads |
| `Images__ApiSecret`                  | Uploads are signed with this server-side — it must never reach the browser |
| `Auth__GoogleClientId`               | Optional. Both Google variables must be set for the button to appear |
| `Auth__GoogleClientSecret`           | Optional                                                      |
| `ANTHROPIC_API_KEY`                  | Optional. Without it, sites use the deterministic template generator |

A blank `DATABASE_URL` is treated as missing: an unresolved Railway variable reference arrives as
an empty string rather than being absent.

**`Email__SmtpHost` is effectively required in production.** Sign-in is by emailed link, so with no
mail provider nobody can get in.

## Domain and DNS

The platform domain is **`csbuild.app`**, registered at Cloudflare, which also hosts the DNS. The
builder is served from the apex and every tenant site from a subdomain of it, so Railway needs
**two** custom domains: `csbuild.app` and `*.csbuild.app`.

`.app` is on the HSTS preload list, so browsers will only ever speak HTTPS to it. There is no
working HTTP fallback while a certificate is pending — the domain is simply unreachable until the
certificate issues, which is expected rather than a misconfiguration.

### Records

Adding each domain in Railway produces the values to enter; Railway gives a wildcard domain two
CNAMEs and a TXT, and **the TXT is not optional** — a wildcard will not verify without it.

| Type  | Name              | Points at                        | Proxy |
| ----- | ----------------- | -------------------------------- | ----- |
| CNAME | `@`               | Railway's host for `csbuild.app` | DNS only |
| CNAME | `*`               | Railway's host for the wildcard  | DNS only |
| CNAME | `_acme-challenge` | Railway's ACME target            | DNS only |
| TXT   | (per Railway)     | Railway's ownership token        | —     |

Every record stays **DNS only** — the grey cloud, not the orange one. Proxying breaks certificate
issuance for the wildcard unless the account has Cloudflare's Advanced Certificate Manager, because
Cloudflare's free wildcard certificate covers one label and the ACME challenge is intercepted.
Cloudflare flattens the apex CNAME on its own, so a CNAME at `@` is fine.

### Reserved subdomains

`TenantResolutionOptions.ReservedSubdomains` is not cosmetic. A DNS wildcard only answers for names
that have no records of their own, so **any label given its own record stops being covered by `*`**
— a tenant handed that name would resolve to infrastructure or to nothing. Mail verification puts
records on `send`, which is why it is reserved. The list also holds names that read as platform
functions (`login`, `billing`, `secure`, …): anyone can sign up, and a phishing page under our own
certificate is worse than a customer not getting their first choice of address. Both tenant
resolution and subdomain suggestion read the one list, so reserving a name blocks it everywhere.

### Mail

Resend verifies `csbuild.app` and sends from `no-reply@csbuild.app`. Verification needs an MX and a
TXT (SPF) on `send`, plus a TXT on `resend._domainkey` — all DNS only. Enter the names **without**
the domain suffix: Cloudflare appends it, so `send`, not `send.csbuild.app`.

Resend recommends a dedicated sending subdomain to isolate reputation, which matters once marketing
mail exists. It does not yet — the only mail is sign-in links and lead notifications, and a sign-in
link from the bare domain is the more trustworthy thing for an owner to receive. If bulk sending is
added later, verify `updates.csbuild.app` separately and leave transactional mail where it is.
