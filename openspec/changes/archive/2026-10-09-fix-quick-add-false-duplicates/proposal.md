# Proposal

## Why

Adding several listings via quick add flags unrelated listings as possible duplicates: two "Senior Developer" roles
at different companies with different URLs get the warning card. The documented rule only matches listings at the
*same company*, so a cross-company match is a bug, and it makes the warning noise the owner learns to ignore.

## What Changes

- Reproduce the false positive with a multi-row batch (several `createListing` calls in one GraphQL request, as quick
  add sends) where rows share a title or role but have different companies. The existing test only covers
  sequential single calls.
- Fix the cause so `possibleDuplicates` only ever contains listings at the same company.
- Role-or-title matching stays as is unless the repro shows the role match is also too loose within one company
  (see design.md, open question).

## Capabilities

### New Capabilities

### Modified Capabilities
- `listings`: "Possible duplicates on create" must hold for batched creates: another company's listing is never
  returned, whatever the title or role.

## Impact

- `src/JobHunt.Api/Features/Listings/ListingMutations.cs` (`CreateListingAsync`).
- `tests/JobHunt.Api.Tests/Features/Listings/ListingTests.cs` (new batch test).
- No schema, migration or GraphQL shape change; no UI change.
