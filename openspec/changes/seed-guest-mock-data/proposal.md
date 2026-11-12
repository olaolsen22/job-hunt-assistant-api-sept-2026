# Proposal

## Why

The guest account logs in to an empty app, which makes it useless as a demo.
Seeding realistic mock data lets visitors immediately see how the app works —
listings in various statuses, a CV profile with experience and skills, a base CV
template, and contacts — without having to create anything themselves.

## What Changes

- Add a `GuestDataSeeder` that runs after `AuthSeeder` and populates the guest
  account with demo data when it has none
- Seed ~8 companies across different industries
- Seed ~10 job listings with varying statuses (Saved, Applied, Interviewing,
  Rejected, Offer) and attributes (seniority, work mode, schedule)
- Seed status changes to give each listing a realistic application timeline
- Seed a complete CV profile: personal details, headline, summary, 3 experience
  entries with bullets, 2 education entries, and profile skills
- Seed one base CV template with a version containing structured CV content
- Seed 3–4 contacts at different companies with interactions
- Seed relevant locations, job roles, and skill records that the above reference
- Use fictional but realistic Norwegian job-market data (Oslo-area tech roles)
- Guard with an idempotency check so re-seeding doesn't duplicate data

## Capabilities

### New Capabilities
- `guest-demo-data`: Seeding and lifecycle of the guest account's demo dataset

### Modified Capabilities
- `seeded-accounts`: Add requirement that the guest account is populated with demo data at startup

## Impact

- `Features/Auth/AuthSeeder.cs` or a new `GuestDataSeeder` — new seeding step
- `Program.cs` — register the new seeder in the startup pipeline
- `AppDbContext` or direct EF Core — bulk insert demo entities
- No schema changes, no migrations — uses existing entities
- No API changes — data appears through existing queries
