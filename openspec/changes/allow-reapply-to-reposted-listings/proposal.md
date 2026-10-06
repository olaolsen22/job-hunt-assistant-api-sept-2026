# Proposal

## Why

A listing is one attempt at one ad, and an ad's URL is unique per user across all listings, archived ones included.
When an ad you were rejected from or withdrew from is reposted at the same URL, it can't be logged again, so the
second attempt has nowhere to live. Reopening the old listing would mix two rounds of timeline and sent documents
and overwrite the ad's dates, which breaks per-application history and later dashboard stats.

The status flow also has no end for a successful attempt: OFFER is a dead end, so a pending offer and a job you took
look the same, and a company withdrawing its offer can't be recorded.

## What Changes

- New status **ACCEPTED**, reached only from OFFER via `recordOutcome(outcome: ACCEPTED)`. Nothing follows it.
- New transition **OFFER → REJECTED** for an offer the company withdraws. Declining an offer stays OFFER → WITHDRAWN.
- A listing is **closed** when its status is final: `REJECTED`, `WITHDRAWN` or `ACCEPTED`. Closed is derived from status,
  never stored.
- Archiving stays a visibility flag only (hide from the default list). It does not close a listing.
- The normalized URL is unique only among listings that are not closed. Creating or updating a listing whose URL
  matches only closed listings succeeds.
- `createListing` returns the earlier attempts at the same URL (closed listings, newest first), so the UI can show
  "you applied to this before".
- A URL matching an open listing (any non-final status, archived or not) is still rejected with
  `DuplicateListingError { existingListingId }`. A saved, archived listing is caught this way, so the UI can offer to
  unarchive it instead of creating a second attempt.
- Earlier attempts are linked by normalized URL only. No new column or FK.
- `undoStatusChange` that would reopen a closed listing while another open listing has the same URL is rejected with
  `DuplicateListingError { existingListingId }`. There is never more than one open listing per URL.

## Capabilities

### New Capabilities
- `listings`: logging job ads as listings, including URL normalization, duplicate rules (open vs closed attempts),
  earlier attempts, possible duplicates, and archive visibility.
- `applications`: the status lifecycle of an attempt (transitions, final statuses, undo) and what "closed" means.

### Modified Capabilities
<!-- None: no specs exist yet. -->

## Impact

- `Features/Applications/StatusChange.cs`: `ApplicationStatus` and `ApplicationOutcome` gain `Accepted`. Enums are stored
  as strings, so no data migration for the value itself.
- `Features/Applications/ApplicationMutations.cs`: transition table (OFFER → ACCEPTED, OFFER → REJECTED); `undoStatusChange`
  respects the open-URL rule.
- `Features/Listings/Listing.cs`: unique index on (UserId, Url) becomes a partial unique index that excludes final
  statuses. Needs a migration.
- `Features/Listings/ListingMutations.cs`: the duplicate check in `ApplyAsync` ignores closed listings, and
  `CreateListingPayload` gains the earlier attempts.
- `schema.graphql` regenerated. The UI gets `ACCEPTED` in two enums and a new payload field; `DuplicateListingError`
  is unchanged.
- Tests: the existing archived-duplicate test (saved + archived) still holds; add accept, offer-withdrawn, re-apply after
  each final status, earlier-attempts payload, and blocked-undo cases.
