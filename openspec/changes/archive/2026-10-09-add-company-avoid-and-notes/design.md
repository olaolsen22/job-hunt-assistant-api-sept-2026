# Design

## Context

`Company` already has `FavoritedAt` and `setCompanyFavorite`, a flag mutation kept separate from `updateCompany`.
Interview notes and status-change notes share `ApplicationMutations.CleanNote` (trim, blank → null) and
`MaxNoteLength` (10,000). `companies` uses projection, filtering and sorting, so new scalar properties on `Company` show
up in the schema, filters and sort inputs with no extra wiring.

## Goals / Non-Goals

**Goals:**
- A filterable avoid flag and a general note on companies, following existing company and notes patterns.

**Non-Goals:**
- Hiding, blocking or warning on listings from avoided companies (the UI badges them).
- Notes on listings, or company contacts (still on the roadmap as their own feature).
- Note history or searching inside notes.

## Decisions

**Flag and notes are separate fields, not one `AvoidReason`.**
A single nullable reason (non-null = avoided) was considered. It can't hold notes on companies you don't avoid, and
un-avoiding would delete the reason. Two fields cost one extra column and keep both uses.

**`AvoidedAt DateTime?` rather than `IsAvoided bool`.**
Same shape as `FavoritedAt` and `ArchivedAt`, so the UI handles all flags one way. "Avoided since" is free history. Uses
`??=` on set, the same as favorites.

**No rule between avoided and favorite.**
Per the owner, they're independent. No validation, and neither mutation reads the other field.

**Notes get their own mutation, not a field on `updateCompany`.**
`updateCompany` is a full replace: a client that doesn't send `notes` would wipe them, and every existing UI call would
need updating. A separate `setCompanyNotes` is non-breaking and matches how the favorite flag is set.

**Reuse `CleanNote` / `MaxNoteLength` from `ApplicationMutations`.**
`InterviewMutations` already does this, so notes rules stay identical everywhere. Moving them to a shared place is a
refactor, out of scope until a third feature is involved.

**Too-long notes → `InvalidCompanyError`.**
That error type already exists on company mutations. A new `InvalidCompanyNotesError` would add schema surface for no
client benefit.

**No index on `AvoidedAt`.**
Per-user company counts are small, and `FavoritedAt` has no index either.

## Risks / Trade-offs

- [Notes become filterable through the default `where` (`notes: {contains: ...}`)] → Harmless and per-user. Not
  documented as a feature.
- [`listings` returning notes for every company is heavy] → Projection only selects requested fields, so the cost only
  applies when the UI asks for notes.

## Migration Plan

One migration, `AddCompanyAvoidAndNotes`: two nullable columns on `Companies`, no backfill. `make migration` backs up
the DB first. Rollback is the generated `Down` (drops both columns, losing any avoid flags and notes set since).
