# Design

## Context

`ListingQueries.GetListings` already computes a due date inside the query for its default order (ASAP = posted + 14,
everything else undated) and hands client `order` to Hot Chocolate via `ISortingContext`. "Today" there is the UTC
date from `TimeProvider`. Lookups (`companies`, `jobRoles`, `skills`) search a stored `NormalizedName` filled by
`Names.Normalize` (trim + `ToUpperInvariant`), because SQLite's `NOCASE`, `LIKE` and `upper()` only fold ASCII. Parse
prompts list enum values from C# (`Values<DeadlineType>()`), so removing `Rolling` removes it from the prompt's schema
line; only the mapping sentence is hand-written. Requirements are in `specs/listings`.

## Goals / Non-Goals

**Goals:**
- One due-date rule used by the default order, `dueWithinDays` and the `dueOn` field.
- Search that matches the lookups' case handling, including æøå.

**Non-Goals:**
- Full-text search, ranking, or searching descriptions.
- Custom filter input types; date ranges stay on Hot Chocolate's `where`.
- Changing how "today" is chosen (UTC date, as today).

## Decisions

**Due date as one expression, computed in the query.**
A single `Expression<Func<Listing, DateOnly>>` holds the rule. The query uses it for ordering and `dueWithinDays`, and
the `dueOn` resolver uses the same expression compiled. EF Core already translates `DateOnly.AddDays` on SQLite (the
current sort relies on it).
- Stored `DueOn` column: would need updating whenever `postedOn`, `deadlineType` or `deadlineOn` change, plus a
  backfill, for no query benefit at this data size.
- Rule duplicated in the UI via `where` with `or`: the 14/30 days would live in two repos.

**`dueOn` as a resolver on `ListingNode`.**
It reads `deadlineType`, `postedOn` and `deadlineOn`, marked `IsProjected` so projection loads them even when the
client didn't select them (same approach as `Company.id` for the counts).

**Search on stored normalized columns.**
New `NormalizedTitle` column, set with `Names.Normalize` wherever `Title` is set. Search normalizes the term the same
way and matches `NormalizedTitle`, `Company.NormalizedName` or `JobRole.NormalizedName` with `Contains`. Blank terms
skip the filter (same effect as the lookups, explicit here).
- `EF.Functions.Like` / `lower()`: ASCII-only folding, fails the æøå requirement.
- A custom SQLite collation: more moving parts than one column.

**Negative `dueWithinDays` → GraphQL error.**
Queries don't use mutation-convention errors; the resolver throws a `GraphQLException` with a readable message, which
becomes a top-level error.

**Two migrations with SQL updates.**
One per task group, so each commit stands alone: `MergeRollingDeadlines` and `AddListingNormalizedTitle`. They run:
- `UPDATE Listings SET DeadlineType = 'Asap' WHERE DeadlineType = 'Rolling'`
- `UPDATE Listings SET NormalizedTitle = UPPER(REPLACE(REPLACE(REPLACE(TRIM(Title), 'æ', 'Æ'), 'ø', 'Ø'), 'å', 'Å'))`

Titles are already stored trimmed. SQLite's `UPPER` handles ASCII; the `REPLACE`s cover Norwegian letters.
- Backfill at startup in C#: exact, but leaves one-off code in `Program.cs` forever.

## Risks / Trade-offs

- [Backfill misses other non-ASCII lowercase letters, e.g. é, ü] → such a title is found only by the term's exact
  uppercase form until the listing is saved again. Acceptable for one user's data; listed in STATUS.
- [Stored parse results still contain `"ROLLING"`] → a result parsed before the upgrade fails GraphQL validation if
  the UI submits it unchanged. The UI should map ROLLING → ASAP for old results, or re-parse. ParseJobs are short-lived.
- [UI breaks on the removed enum value] → BREAKING in the proposal; ship the UI change together with this one.
- [Default order changes for UNSPECIFIED listings (no longer last among upcoming)] → intended; covered by the updated
  sort test.

## Migration Plan

`MergeRollingDeadlines` runs the `Rolling` → `Asap` update; its rollback does nothing, because the distinction is gone
by design. `AddListingNormalizedTitle` adds the column and backfills it; rollback drops the column.
