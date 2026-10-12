# Design

## Context

See proposal.md for motivation and specs/listings/spec.md for the contract. `ListingMutations.NormalizeUrl` currently owns normalization privately; create/update already use it. `ListingQueries` exposes paged listing discovery but defaults to excluding archived rows. `AppDbContext` scopes listings through the authenticated user's global filter, and `/graphql` requires the existing Identity cookie. Integration tests use `ApiFactory` with owner/guest clients, an in-memory SQLite database, and fake AI/page clients. `SchemaTests` maintains `schema.graphql`.

## Goals / Non-Goals

**Goals:** Offer a bounded query that returns deterministic decisions for collected URLs without needing AI-produced title/company/date fields. Keep normalization consistent with create/update and use one database lookup per batch.

**Non-Goals:** No persistent seen-URL cache, provider-specific ad identity, fuzzy comparisons, new auth mode, or reservation semantics. The external automation invokes the query through an ordinary authenticated client; this API change cannot configure an unspecified external service.

## Decisions

### GraphQL query with a URL-only contract

Add `CheckListingUrlsAsync` under the Listings slice with a result record. Use the public names and fields defined in the delta spec. Preserve `url` as submitted and return `normalizedUrl` separately. `existingListingIds` and `duplicateOfIndex` are independent; derive `shouldProcess` from both. This permits direct mapping by input index to the caller's richer master-list rows.

Alternative: fetch all saved listings and deduplicate solely in a client script. That requires paging archived and active data and duplicating normalization outside the API. A REST endpoint would conflict with the repository's GraphQL domain API convention.

### Shared normalization without expanding URL equivalence

Extract the current normalization and tracking-key predicate to a small internal helper within `Features/Listings`, and reuse it for create/update and preflight. Preserve current behavior exactly, including meaningful parameter order and path case; do not lower-case entire URLs or infer that different boards carry the same job. Input validation belongs to the query boundary, with 1–100 rows and 2048-character trimmed URL bounds. Use `GraphQLException` for query validation errors, consistent with `dueWithinDays`; include an input index for invalid URLs and return no partial check results.

Alternative: copy the normalization into the resolver. It is initially small but could drift from create/update. Additional canonicalization would change existing equivalence rules and is outside this change.

### One bounded lookup and a batch dictionary

Validate and normalize every input before reading the database. Collect distinct normalized URLs and issue one no-tracking query against `db.Listings` for matching URLs, projecting only `Id` and `Url`. Keep the global user filter; do not route through `GetListings`, which applies archive defaults. Group matching ids by URL in ascending id order. Walk inputs once with an ordinal dictionary of the first index per normalized URL, returning ordered results. Honor request cancellation.

Alternative: one database lookup per URL is unnecessary; a full table read transfers unrelated rows. Existing indexes and a maximum of 100 URLs are sufficient for this personal application; no migration is planned.

### Saved history counts as seen for discovery

All persisted listing states suppress the automated recommendation. This is a discovery policy, not the creation eligibility rule: closed-only matches still permit an explicit new attempt through `createListing`. This assumption follows the user's choice to compare against saved listings, and makes no claim that a changed ad at the same URL is unchanged content.

Alternative: matching only open listings mirrors create constraints but repeatedly sends already known closed ads to the AI. Cross-board company/title heuristics could skip distinct openings and require information the AI has not yet extracted.

### Caller filters before AI work

Document this query in `docs/listing-url-preflight.md` during implementation:

```graphql
query CheckCollectedUrls($urls: [String!]!) {
  checkListingUrls(urls: $urls) {
    index
    url
    normalizedUrl
    existingListingIds
    duplicateOfIndex
    shouldProcess
  }
}
```

Example variables use the supplied NAV URL:

```json
{
  "urls": [
    "https://arbeidsplassen.nav.no/stillinger/stilling/93fec960-d51d-46b1-8eb8-8ccd08608bef"
  ]
}
```

The caller collects links, sends up to 100 per query, and forwards only eligible rows to the AI. For larger lists, preserve a set of returned normalized URLs across chunks within the run, allowing only the first occurrence across the whole list; indices are local to each chunk. Nothing about unsaved inputs persists between runs. Authenticate using `/api/auth/login` and retain the existing session cookie, with credentials outside committed files. Explain that a remotely hosted scheduler must have network access to the API and perform this tool/script step; putting instructions in an AI prompt alone does not perform a local check. Actual external-service setup is outside this repository's scope.

## Risks / Trade-offs

- [Unsaved jobs can recur across runs] → Explicitly document that only persisted listings and repeats in the current run are covered.
- [Closed attempts or reused ad URLs may be worth revisiting] → Return matching ids for caller inspection and preserve deliberate reapplication; no content-change detection is promised.
- [A different URL can describe the same job] → Document exact normalized URL matching; preserve the existing possible-duplicate warnings at create time.
- [Concurrent requests can both recommend processing] → Keep create-time duplicate checks authoritative; the query makes no reservation.
- [External service lacks API access or cookie handling] → Document prerequisites and the script/tool boundary; no token reduction occurs until the automation invokes the check before AI processing.
- [Normalization extraction can affect existing writes] → Retain behavior and cover create/update regressions alongside preflight cases.

## Migration Plan

No database migration. Implement the helper and query, refresh and review the additive schema change through the schema test, then run the full test suite once. Deploy the API before the external caller enables the query. Rollback consists of disabling that caller step and reverting the additive API change; no stored data requires restoration.
