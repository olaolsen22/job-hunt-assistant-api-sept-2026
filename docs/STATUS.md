# Project Status

Updated: 2026-09-26 · Last logged commit: 0a9c9e8

## Milestones
| # | Milestone | Status |
|---|---|---|
| M0 | Scaffold, CI, Scalar, EF Core + SQLite, Docker | ✅ done |
| M1 | Identity + cookie auth, Owner/Guest seed | ✅ done (uncommitted; user query filter moves to M2) |
| M2 | Applications, status history, notes, contacts | ⬜ |
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

## Entities
- `AppUser : IdentityUser` (string Id) + Identity tables (incl. UserLogins/UserTokens for future SSO/2FA)
- Roles: `Owner`, `Guest` (`Features/Auth/AppUser.cs`)
- `AppDbContext : IdentityDbContext<AppUser>` in `Data/`; configs via `IEntityTypeConfiguration` + `ApplyConfigurationsFromAssembly`. Migrations applied at startup, then `AuthSeeder` runs.
- Migrations: `AddSamples`, `RemoveSamples`, `AddIdentity`

## Config keys
- `ConnectionStrings:Default` — `Data Source=jobhunt.db` (dev), `/data/jobhunt.db` (Docker)
- `Seed:OwnerEmail`, `Seed:OwnerPassword` — required; user-secrets (dev), `.env` → compose (Docker). Password re-applied on startup.
- `Seed:GuestEmail` — `guest@jobhunt.local`; guest has no password

## Stack
- .NET SDK 10.0.112, xunit 2.9.3, Scalar.AspNetCore 2.17.10, OpenApi/Mvc.Testing 10.0.12
- Versions pinned in `Directory.Packages.props`; shared props in `Directory.Build.props`
- `dotnet-ef` as local tool (`dotnet-tools.json`)
- Tests: `ApiFactory` = WebApplicationFactory + named in-memory SQLite per factory
- Docker: `docker compose up` → :8080, non-root `app` user, volume `jobhunt-data:/data`

## Decisions log
- 2026-09-26 — Plan agreed (see docs/PLAN.md). Logger is a file + `/log-progress` skill, not an agent, to save tokens.
- 2026-09-26 — Auth: `AddIdentityCookies` + `requiresTwoFactor` in login contract so SSO/2FA stay additive; no email verification until public (PLAN §3, §9).
- 2026-09-26 — Hybrid API: REST for auth/health, GraphQL (Hot Chocolate 16) for domain data, subscriptions over SSE replace the custom SSE broker (PLAN §6–7). Package added with the first GraphQL feature.
- 2026-09-26 — Cookie `jobhunt.auth`: HttpOnly, SameSite=Lax, Secure except in Development; lockout 5 tries/5 min; login limiter 10/min per IP.

## Next up
- Applications, status history, notes, contacts as GraphQL (add Hot Chocolate); global `UserId` query filter + cascade delete from `AppUser`.
- M8: forwarded headers (rate limiter IP behind proxy), DataProtection keys volume.
