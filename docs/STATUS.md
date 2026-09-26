# Project Status

Updated: 2026-09-26 · Last logged commit: (none)

## Milestones
| # | Milestone | Status |
|---|---|---|
| M0 | Scaffold, CI, Scalar, EF Core + SQLite, Docker | ✅ done (uncommitted) |
| M1 | Identity + cookie auth, Owner/Guest seed | ⬜ |
| M2 | Applications, status history, notes, contacts | ⬜ |
| M3 | Structured CVs | ⬜ |
| M4 | Dashboard stats | ⬜ |
| M5 | LLM layer, usage logging, settings | ⬜ |
| M6 | Parse queue + SSE + cache | ⬜ |
| M7 | Guest sandbox reset + AI cap | ⬜ |
| M8 | Deploy, README | ⬜ |

## Endpoints
- `GET /api/health` — built-in health checks incl. DB connectivity
- `GET /api/samples` — **temporary** scaffold check, 3 seeded rows. Remove in M1 (entity, endpoint, test, + a removal migration).
- Dev only: `/openapi/v1.json`, `/scalar`

## Entities
- `Sample` (temporary) — Id, Name. Seeded via `HasData`.
- `AppDbContext` in `Data/`; configs via `IEntityTypeConfiguration` + `ApplyConfigurationsFromAssembly`. Becomes IdentityDbContext in M1. Migrations applied at startup.
- Migrations: `AddSamples`

## Config keys
- `ConnectionStrings:Default` — `Data Source=jobhunt.db` (dev), `/data/jobhunt.db` (Docker)

## Stack
- .NET SDK 10.0.112, xunit 2.9.3, Scalar.AspNetCore 2.17.10, OpenApi/Mvc.Testing 10.0.12
- Versions pinned in `Directory.Packages.props`; shared props in `Directory.Build.props`
- `dotnet-ef` as local tool (`dotnet-tools.json`)
- Tests: `ApiFactory` = WebApplicationFactory + named in-memory SQLite per factory
- Docker: `docker compose up` → :8080, non-root `app` user, volume `jobhunt-data:/data`

## Decisions log
- 2026-09-26 — Plan agreed (see docs/PLAN.md). Logger is a file + `/log-progress` skill, not an agent, to save tokens.

## Next up
- M1: remove Samples; Identity + cookie auth, Owner/Guest seed, user query filter.
