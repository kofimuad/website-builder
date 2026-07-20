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
| <http://joesplumbing.localhost:5184>   | A published demo tenant site                   |
| <http://nosuchbusiness.localhost:5184> | The "no website here yet" page                 |
| <http://localhost:5184/healthz>        | Health check                                   |

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

## Deployment

Deployed on Railway from `main`. The service needs:

| Variable                             | Notes                                                        |
| ------------------------------------ | ------------------------------------------------------------ |
| `DATABASE_URL`                       | Reference the Postgres service, e.g. `${{Postgres.DATABASE_URL}}` |
| `TenantResolution__PlatformDomain`   | The domain tenant subdomains hang off. Defaults to `localhost` |

A blank `DATABASE_URL` is treated as missing: an unresolved Railway variable reference arrives as
an empty string rather than being absent.
