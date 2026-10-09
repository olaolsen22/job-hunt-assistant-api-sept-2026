# Proposal

## Why

The UI is getting a listings search box and filters, including "closing soon" and date ranges. Today the only text
search is the generic `contains` filter, which is case-sensitive on SQLite (and SQLite's `LIKE`/`lower()` don't fold
æøå). "Closing soon" has no single definition: the default sort treats ASAP as posted + 14 days and the rest as
undated, so a UI-built filter would duplicate that rule and drift from it. ROLLING and ASAP mean the same thing in
practice, which only gives the parser a distinction with no effect.

## What Changes

- **BREAKING** `DeadlineType` drops `ROLLING`; it becomes `DATE | ASAP | UNSPECIFIED`. Existing `Rolling` rows become
  `Asap`. Both parse prompts map "Løpende"/"fortløpende" to ASAP and bump their versions (full 3 → 4, quick 1 → 2).
- Every listing has an **effective due date**, owned by the server:
  - `DATE` → `deadlineOn`
  - `ASAP` → `postedOn` + 14 days
  - `UNSPECIFIED` → `postedOn` + 30 days

  A listing past its due date counts as expired. This affects sorting and filtering only; status, archive and data are
  untouched.
- New field `Listing.dueOn` exposes the effective due date, so UI badges match the server's sort and filter.
- The default listings order uses the effective due date: upcoming soonest first, expired last. There is no longer an
  "undated" group.
- New argument `listings(dueWithinDays: Int)`: due between today and today + N; expired listings excluded.
- New argument `listings(search: String)`: case-insensitive (including æøå) match on title, company name and job role
  name. Description is not searched. Combines with `where`.
- Posted-date and deadline ranges stay on the existing `where` (`postedOn`/`deadlineOn` with `gte`/`lte`); no new API,
  covered by tests. The deadline range only matches listings with a set date.

## Capabilities

### New Capabilities
- `listings`: searching, filtering and ordering listings, deadline types, and the effective due date. If
  `allow-reapply-to-reposted-listings` is archived first, this becomes a modification of the `listings` spec it creates.

### Modified Capabilities
<!-- None: no specs exist yet. -->

## Impact

- `Features/Listings/Listing.cs`: `DeadlineType` loses `Rolling`; new `NormalizedTitle` column (set alongside `Title`).
  Migration: `Rolling` → `Asap`, backfill `NormalizedTitle`.
- `Features/Listings/ListingQueries.cs`: due-date expression shared by the sort, `dueOn` and `dueWithinDays`; `search`
  argument.
- `Features/Listings/ListingMutations.cs`: set `NormalizedTitle` on create/update.
- `Features/Listings/ListingParsePromptQueries.cs`: deadline mapping and both version constants.
- `schema.graphql` regenerated; the UI removes `ROLLING` and can use `dueOn`, `search` and `dueWithinDays`.
- Tests: `ListingParsePromptTests` (asserts `"ROLLING"` today), `ListingSortingTests`, new search, due-date and
  date-range filter tests.
