# Accounts and access

Who can touch a site, and why the checks sit where they do (WB-15).

## The three layers

| Layer      | Answers                                  | Lives in                        |
| ---------- | ---------------------------------------- | ------------------------------- |
| **Owner**  | Who is signed in?                        | Auth cookie → `Owner` row       |
| **Tenant** | Which business is this?                  | `Tenant.OwnerId`                |
| **Site**   | Which of that business's sites?          | `Site.TenantId`                 |

`Owner` is deliberately **not** `ITenantOwned` and has no query filter. It is the thing that grants
tenant scope, so it has to be readable before any tenant is known.

`Tenant.OwnerId` is nullable. Tenants created before sign-in existed have no owner and can never be
managed — their published sites still serve normally. This is intentional: an unmanageable live
site is a far better failure than a site anyone can claim.

## The gate

`SiteManagementService.LoadAsync(siteId, ownerId)` is the single choke point. Every management page
goes through it, and it does two things **in this order**:

1. Join the site to `Tenants` filtered by `OwnerId`. No match → return null.
2. *Only then* assign `TenantContext.TenantId`.

The order is the whole point. Everything downstream — the editor, the leads inbox, publishing —
trusts `TenantContext`, so granting scope before checking ownership would hand a stranger the keys
and check the lock afterwards.

Not-found and not-yours return the same `null`. A distinct "forbidden" would confirm that a site id
is real, which is the first step in enumerating them.

`SitePreview` repeats the join rather than calling `LoadAsync` because it is a Razor Page with a
real `HttpContext`, not a component.

## Why the owner id is a parameter

Inside an interactive Blazor circuit there is no `HttpContext` — its response finished when the page
loaded. A service reaching for `IHttpContextAccessor` would see null and, if it treated that as
"anonymous", fail open on every interactive render. Components read identity from the cascading
`AuthenticationState` and pass it in, so the gate cannot be fed a silent null by accident.

## Why sign-in is not in a component

Writing an auth cookie needs a live response, which a circuit does not have. So:

- `/auth/*` are minimal API endpoints — they sign in, sign out and redirect.
- `/signin` is a **static** server-rendered component. Blazor handles its antiforgery token, and
  there is no circuit to lose the typed address to.
- Everything else only ever *reads* auth state.

## Magic links

- 32 random bytes, base64url. Only a SHA-256 hash is stored — a database dump must not let anyone
  sign in as the addresses in it.
- Single use (`ConsumedUtc`), 15-minute expiry, rate limited per address.
- A rate-limited request still shows "check your email". Saying "too many attempts" would confirm
  which addresses have accounts.
- Return URLs are validated as local paths before storage. An unchecked one turns sign-in into an
  open redirect — a real domain in the link, someone else's page at the end of it.

Email is the identity: Google and magic link for one address resolve to one `Owner`. Google's
subject claim is recorded but is not the key, because an address can be reassigned inside a
Workspace domain.

## Onboarding

The interview at `/start` is anonymous — asking for an account before anyone has seen anything is
how you lose them. Sign-in is required at the moment there is something to save:

1. Interview completes → answers go to `OnboardingDraftStore` (in memory, one hour, single use).
2. Redirect to `/signin?returnUrl=/start?claim=<key>`.
3. Back at `/start`, the answers are claimed and the site is built with the owner attached.

The stash is in memory on purpose. A restart loses a pending draft, and the cost is re-answering
seven questions — nothing is persisted until sign-in completes.

## Still open

- No roles. An owner either owns a tenant or does not; there is no staff access or delegation.
- No account deletion or tenant transfer. The `Tenant → Owner` FK is `Restrict`, so deleting an
  owner with live sites fails rather than cascading customer websites into oblivion.
- Admin and impersonation (WB-40/41) have no home here yet.
