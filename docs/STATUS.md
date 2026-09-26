# Project Status

Updated: 2026-09-26 · Last logged commit: c7f5741

## Milestones
| # | Milestone | Status |
|---|---|---|
| M0 | Scaffold, CI, Scalar, EF Core + SQLite, Docker | ✅ done |
| M1 | Identity + cookie auth, Owner/Guest seed | ✅ done |
| M2 | Listings (+ companies, job roles, skills, languages); then tracking: status history, notes, contacts | 🟡 listings + locations done; tracking design pending |
| M3 | Structured CVs | ⬜ |
| M4 | Dashboard stats | ⬜ |
| M5 | LLM layer, usage logging, settings | ⬜ |
| M6 | Parse queue + SSE + cache | ⬜ |
| M7 | Guest sandbox reset + AI cap | ⬜ |
| M8 | Deploy, README | ⬜ |

## Endpoints
All endpoints require auth (fallback policy) unless marked anon.
- `GET /api/health` (anon) — built-in health checks incl. DB connectivity
- `POST /api/auth/login` (anon, rate-limited) — `{email, password}` → `{requiresTwoFactor, user}`; 401 bad creds, 429 locked out
- `POST /api/auth/guest` (anon, rate-limited) — one-click guest sign-in, same response
- `POST /api/auth/logout` (anon) — 204
- `GET /api/auth/me` — `{id, email, role}`
- Dev only (anon): `/openapi/v1.json`, `/scalar`
- `POST /graphql` (Hot Chocolate 16; Nitro IDE on GET in browser). All user data scoped by global query filter.
  - SDL committed at `schema.graphql` (repo root) for the UI; `SchemaTests` regenerates it and fails when it's stale
  - Query: `listings` / `companies(search)` / `jobRoles(search)` / `skills(search)` / `locations(search: city)` — cursor paging (default 10, `totalCount`), projection, `where`, `order`; `listingById(id)`
  - `listingParsePrompt { version instructions }` — copy-paste prompt for external AIs (UI appends `<posting>…</posting>`); enums from C#, user's role/skill names (max 100 each), today's date. `ListingParsePromptQueries.Version` = 1
  - `Company|JobRole|Skill|Location.listingCount` — computed via DataLoader (one GROUP BY per page)
  - Mutation (conventions, typed errors): `createListing(input:{listing})`, `updateListing(input:{id, listing})` (full replace incl. skills/languages), `deleteListing(input:{id})` → `id`, `updateCompany(input:{id, name, location: LocationInput})`, `saveLocation(input:{city?, countryCode, isTarget})` (find-or-create), `setListingFavorite(input:{id, isFavorite})` (keeps first FavoritedAt; UpdatedAt untouched)
  - Errors: `InvalidListingError`, `ListingNotFoundError`, `InvalidCompanyError`, `CompanyNotFoundError`, `InvalidLocationError`
  - `ListingInput` takes `companyName`/`jobRoleName`/skill `name`/`locations: [{city?, countryCode}]` → found case-insensitively or created. Defaults: skill `level: 3`, `isRequired: true`, `deadlineType: UNSPECIFIED`

## Entities
- `AppUser : IdentityUser` (string Id) + Identity tables (incl. UserLogins/UserTokens for future SSO/2FA)
- Roles: `Owner`, `Guest` (`Features/Auth/AppUser.cs`)
- `AppDbContext : IdentityDbContext<AppUser>` in `Data/`; configs via `IEntityTypeConfiguration` + `ApplyConfigurationsFromAssembly`. Migrations applied at startup, then `AuthSeeder` runs.
- `Listing` (`Features/Listings/Listing.cs`): Title, CompanyId, JobRoleId, Seniority?, EmploymentType?, Schedule?, WorkMode?, Location?, Description?, Url?, PostedOn (required, not future; UI pre-fills today) / DeadlineOn? (DateOnly), DeadlineType, PostingLanguage?, Url (required, http/https only), CreatedAt/UpdatedAt (UTC), FavoritedAt? (null = not favorited)
  - `ListingLocation` PK (ListingId, LocationId)
  - `ListingSkill` PK (ListingId, SkillId): Level 1–5, MinYears? (as stated in ad), IsRequired · `ListingLanguage` PK (ListingId, Language): Level CEFR?, IsRequired
- Lookups per user, unique (UserId, NormalizedName): `Company` (+LocationId? head office, SetNull), `JobRole` (core role, English), `Skill`.
- `Location` (`Features/Locations`): City? (null = whole country), NormalizedCity ("" if none), CountryCode (ISO 3166, validated via `RegionInfo`), IsTarget; unique (UserId, CountryCode, NormalizedCity). `LocationLookup.FindOrCreateAsync` shared by listings/companies/saveLocation
- Hot Chocolate has a global `Location` type: files referencing ours use `using Location = JobHunt.Api.Features.Locations.Location;` `Names.Normalize` = Trim + ToUpperInvariant
- Enums stored as strings (`ConfigureConventions`). Global filter `UserId == CurrentUserId` (from `IHttpContextAccessor`); child rows filtered via Listing
- FKs: AppUser → all user tables cascade; Listing → Company/JobRole NoAction; children cascade from Listing/Skill
- Migrations: `AddSamples`, `RemoveSamples`, `AddIdentity`, `AddListings`, `AddLocations` (dropped free-text Location columns; no rows existed), `RequireUrlAddMinYears`, `RequirePostedOn` (backfills null PostedOn from CreatedAt), `AddListingFavorites`

## Config keys
- `ConnectionStrings:Default` — `Data Source=jobhunt.db` (dev), `/data/jobhunt.db` (Docker)
- `Seed:OwnerEmail`, `Seed:OwnerPassword` — required; dev defaults in `appsettings.Development.json` (override with user-secrets), `.env` → compose (Docker). Password re-applied on startup.
- `Seed:GuestEmail` — `guest@jobhunt.local`; guest has no password

## Stack
- .NET SDK 10.0.112, xunit 2.9.3, Scalar.AspNetCore 2.17.10, OpenApi/Mvc.Testing 10.0.12, HotChocolate 16.6.7 (AspNetCore, Data.EntityFramework, Types.Analyzers)
- GraphQL types source-generated: `[assembly: Module("JobHuntTypes")]` → `AddJobHuntTypes()`. DbContext via `AddDbContextFactory` + `RegisterDbContextFactory`
- Versions pinned in `Directory.Packages.props`; shared props in `Directory.Build.props`
- `dotnet-ef` as local tool (`dotnet-tools.json`)
- Tests: `ApiFactory` = WebApplicationFactory + named in-memory SQLite per factory; `GetOwnerClientAsync`/`GetGuestClientAsync` (cached, login is rate-limited); `client.GraphQLAsync(query, variables)` fails on top-level errors
- Docker: `docker compose up` → :8080, non-root `app` user, volume `jobhunt-data:/data`

## Decisions log
- 2026-09-26 — Plan agreed (see docs/PLAN.md). Logger is a file + `/log-progress` skill, not an agent, to save tokens.
- 2026-09-26 — Auth: `AddIdentityCookies` + `requiresTwoFactor` in login contract so SSO/2FA stay additive; no email verification until public (PLAN §3, §9).
- 2026-09-26 — Hybrid API: REST for auth/health, GraphQL (Hot Chocolate 16) for domain data, subscriptions over SSE replace the custom SSE broker (PLAN §6–7). Package added with the first GraphQL feature.
- 2026-09-26 — Cookie `jobhunt.auth`: HttpOnly, SameSite=Lax, Secure except in Development; lockout 5 tries/5 min; login limiter 10/min per IP.
- 2026-09-26 — Listings before tracking. Listing title kept as in the ad; core role is a separate English `JobRole` (seniority + tech split out) so NO/EN ads line up.
- 2026-09-26 — Skills/companies/roles are per-user lookups (guest can't pollute; nightly reset just deletes rows). Listing skill level 1–5, no 0 (missing CV row = 0).
- 2026-09-26 — Deadline = enum (Date/Asap/Rolling/Unspecified) + DateOnly; EmploymentType and Schedule split because NO ads combine them ("Fast, deltid").
- 2026-09-26 — Locations: city + ISO country code (UI names via Intl.DisplayNames), many per listing, country-only rows for remote; `IsTarget` for dashboard widget.
- 2026-09-26 — Parsing via external AI: API serves the prompt, UI stitches ad + prompt, user pastes JSON back to pre-fill the form. Same prompt will drive the server parser. Skill level rubric: higher of years/wording.
- 2026-09-26 — Tracking design (not built): current Status + StatusChangedAt on Listing, history rows with user-settable OccurredAt (PLAN §4).

## Next up
- Tracking (status history, notes, contacts) once the owner's adjustments are in.
- `topSkills` (GROUP BY skill: listingCount, requiredCount, averageLevel) with dashboard stats; target-cities widget uses existing `locations`/`listings` filters.
- Favorites: drag-to-rank (`FavoriteRank`) or named lists only if wanted.
- M8: forwarded headers (rate limiter IP behind proxy), DataProtection keys volume.
