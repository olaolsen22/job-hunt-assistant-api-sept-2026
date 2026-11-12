# Design

## Context

The guest account is created by `AuthSeeder` with no password (one-click login
only). Currently it starts empty. All user-owned entities have `UserId` and a
global query filter, so seeded data needs the guest's Identity user ID.

Existing entities support all the demo data we need — no schema changes or
migrations required. The `Names.Normalize()` helper handles normalized name
fields. `TimeProvider` should be injected for timestamps.

## Goals / Non-Goals

**Goals:**
- Populate the guest account with enough data to demonstrate every major feature
  area: listings pipeline, CV profile, base CV templates, contacts
- Make the seeder idempotent and fast (single `SaveChangesAsync` batch)
- Use realistic Norwegian tech job-market data

**Non-Goals:**
- Seeding tailored CV drafts, cover letters, or LLM usage (these depend on
  external LLM calls and aren't needed for a demo)
- Seeding documents or writing samples
- Making the demo dataset configurable
- Seeding dashboard widget preferences

## Decisions

**Separate `GuestDataSeeder` class** rather than extending `AuthSeeder`.
AuthSeeder handles Identity accounts; demo data is domain entities through
`AppDbContext`. Different concerns, different dependencies. Called from
`Program.cs` after `AuthSeeder.SeedAsync`.

**Idempotency via listing count check.** If the guest has any listings, skip
entirely. This is simpler and faster than per-entity upserts, and the guest
account's data is disposable — a full reset means deleting all guest data and
restarting. The check uses `AnyAsync` on `Listings` filtered by UserId.

**Single `SaveChangesAsync` call.** Add all entities to the context, then save
once. EF Core assigns IDs, so navigation properties handle FK wiring without
manual ID management. Order: locations → companies → job roles/skills →
listings → status changes → profile → entries/bullets → profile skills →
base CV → contacts.

**Hardcoded data in the seeder class.** No JSON files or config — the data is
small, domain-specific, and only used once. A static method returning the entity
graph keeps it contained.

**Fictional Norwegian company names.** Use names like "Nordvik Solutions",
"Fjordtech AS", "Brygge Digital" — clearly fictional, plausible-sounding, in the
Oslo tech scene.

## Risks / Trade-offs

**Maintenance burden**: Entity changes may break the seeder. Mitigated by the
seeder using the same entity classes — compiler errors catch removed/renamed
properties. No separate DTO layer.

**Data realism vs. simplicity**: The demo data won't have parsed listing
descriptions or LLM-generated content. Acceptable — the goal is to show the UI
populated, not to demonstrate AI features.

**Single idempotency gate**: If someone deletes all guest listings but not other
data, a restart would re-seed and create duplicate companies/contacts. Acceptable
for a demo account — the alternative (per-entity checks) adds complexity for a
scenario that doesn't matter.
