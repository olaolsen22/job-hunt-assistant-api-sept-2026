# Design

## Context

`Listing` holds the ad and its application state. `Status` is stored on the listing and changed only by
`ApplicationMutations.ChangeStatus` (and undo), which keeps it in sync with the `StatusChange` timeline. Enums are
stored as their C# names (`"Rejected"`). URL uniqueness is enforced twice today: a lookup in `ListingMutations.ApplyAsync`
that throws the typed `DuplicateListingException`, and a unique index on (UserId, Url) as a backstop. Requirements are
in `specs/listings` and `specs/applications`.

## Goals / Non-Goals

**Goals:**
- The database still guarantees one open listing per URL, not only the C# check.
- One definition of "final status" in code, used by every rule that needs it.

**Non-Goals:**
- An "earlier attempts" view per company (that is a plain `where` on company today).
- Changing how mutations load listings (`FindWithDetailsAsync`); no new includes either.
- Dashboard use of ACCEPTED.

## Decisions

**Partial unique index instead of a stored flag or a code-only check.**
The (UserId, Url) index gets a filter: `"Status" NOT IN ('Rejected', 'Withdrawn', 'Accepted')`. SQLite supports partial
indexes and EF Core maps them with `HasFilter`.
- Stored `IsClosed` column: a second copy of state the status already holds, and one more thing undo must keep in sync.
- Code-only check: simplest, but loses the database backstop the project has today.

**Final statuses defined once.**
A static set (e.g. `ApplicationStatus` final values next to the enum) is used by the duplicate check, the undo check
and tests. The index filter is a SQL string and can't reference it, so tests pin the two together: re-applying
succeeds after each final status (the index must not block it), and creating fails while the other listing is in each
open status (the C# check must catch it).

**Previous attempts found by URL, no FK.**
`createListing` queries closed listings with the same normalized URL. A `PreviousListingId` FK would need maintaining
on undo and delete, and the URL already identifies the ad.

**Payload shape.**
`CreateListingPayload` gains `previousAttempts: [Listing!]` (newest first; nullable like `possibleDuplicates`, because
mutation conventions null the payload fields on errors). `possibleDuplicates` excludes listings
with the same URL, so a listing shows up in one list only. `DuplicateListingError` is unchanged.

**Explicit outcome mapping.**
`recordOutcome` maps `ApplicationOutcome` with a `_ => Withdrawn` default today. Adding `Accepted` makes every case
explicit, so a new outcome can't silently become WITHDRAWN.

**Undo check only when leaving a final status.**
Undo checks for another open listing with the same URL only when the step being removed has a final status. Undoing
any other step keeps the listing open, so it can't create a conflict.

**Update of a closed listing skips the URL check.**
The duplicate check in `ApplyAsync` runs only when the listing being saved is open (new listings are SAVED, so always
open). Two listings sharing a URL is fine when at most one is open.

## Risks / Trade-offs

- [Index filter and C# final-status set drift apart] → both sides covered by tests per status, as above.
- [Race: two creates with the same URL at once] → the index rejects the second with a database error instead of
  `DuplicateListingError`. Same as today; acceptable for one user.
- [UI treats ACCEPTED as unknown] → additive enum value; the UI updates its hand-written types from `schema.graphql`.

## Migration Plan

One migration: drop the unique index on (UserId, Url), recreate it with the filter. No data changes: the old index was
stricter, so existing rows already satisfy the new one. Rollback recreates the unfiltered index, which only fails if a
re-applied listing exists by then.
