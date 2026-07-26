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
| `TenantResolution__PlatformDomain`   | The domain tenant subdomains hang off. Defaults to `localhost` |
| `Platform__PublicBaseUrl`            | Absolute URL of the builder, e.g. `https://sitely.app`. Links in email are built from it |
| `Email__SmtpHost`                    | Leave unset and email is logged, not sent — sign-in becomes unusable in production |
| `Email__SmtpPort`                    | Defaults to 587                                               |
| `Email__SmtpUser` / `Email__SmtpPassword` | Provider credentials                                     |
| `Email__FromAddress`                 | Must be an address the provider has authorised, or mail bounces |
| `Auth__GoogleClientId`               | Optional. Both Google variables must be set for the button to appear |
| `Auth__GoogleClientSecret`           | Optional                                                      |
| `ANTHROPIC_API_KEY`                  | Optional. Without it, sites use the deterministic template generator |

A blank `DATABASE_URL` is treated as missing: an unresolved Railway variable reference arrives as
an empty string rather than being absent.

**`Email__SmtpHost` is effectively required in production.** Sign-in is by emailed link, so with no
mail provider nobody can get in.
