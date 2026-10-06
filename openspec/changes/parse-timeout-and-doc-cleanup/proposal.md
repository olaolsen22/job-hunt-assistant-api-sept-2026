# Proposal

## Why

`parseListing` only handles provider failures that surface as `HttpRequestException`. A provider timeout surfaces as
`TaskCanceledException`, so the mutation fails with a generic top-level error and neither the ParseJob nor its
`LlmUsage` row is saved, breaking the rule that every LLM call is logged. Separately, `docs/PLAN.md` still describes an
`Application` entity and API that were replaced during the build, contradicts itself on CV storage, and doesn't record
the follow-ups found in the project review; STATUS.md's header points at an old commit. Stale docs are the main reason
the project reads as unplanned.

## What Changes

- A provider timeout during parsing marks the ParseJob FAILED with a readable "took too long" error and logs an
  `LlmUsage` row with error code `timeout`. A request cancelled by the client is unchanged (not a provider failure).
- `docs/PLAN.md` rewritten where it no longer matches: data model (§4: listing as one attempt, status on listing,
  documents per listing, closed/final statuses, deadline types), API surface (§7: real query and mutation names), and
  milestones (§9: M3 CVs use relational tables, not JSON columns; CV generation produces a document on a listing).
- `docs/PLAN.md` records the deferred follow-ups: guest AI cap before deploy, user scoping for background work (parse
  worker, guest reset) since the user filter needs a request, and storing identical CV files once (content hash).
- `docs/STATUS.md` header shows the real last logged commit.

## Capabilities

### New Capabilities
- `listing-parsing`: server-side parsing of pasted job ads into listing JSON, its failure modes and usage logging.

### Modified Capabilities
<!-- None: no specs exist yet. -->

## Impact

- `Features/Parsing/ListingParser.cs`: one more handled failure.
- Tests: `ParseTests` gains a timeout case.
- `docs/PLAN.md`, `docs/STATUS.md`. No API, schema or migration change.
