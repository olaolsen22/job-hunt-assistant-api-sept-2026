# Proposal

## Why

`parseListingFromUrl` only works on a saved listing, because it reads the URL the listing stores. When adding a new
single listing there is no listing yet, so the owner still has to open the ad and paste its text. The same fetch and
extraction should run on a URL the owner types into the add form, ending in the same preview/edit step.

## What Changes

- New GraphQL mutation `parseUrl(url: String!)`: validates the URL, fetches it with the existing `ListingPageFetcher`
  (same safety policy and extraction) and runs a Full-mode parse. Returns the same `ParseJob` as `parseListing`.
- Invalid URLs (not absolute `http`/`https`, or longer than 2048 characters) and unreadable pages return the
  existing `InvalidParseRequestError`, with no job created.
- `parseListingFromUrl(listingId)` is unchanged; it stays the path for saved listings so the stored URL remains the
  source of truth there.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
- `listing-url-parsing` (from the unarchived `parse-listing-from-url` change): adds parsing a URL the client supplies
  for a listing that isn't saved yet. The "client SHALL NOT supply a URL" rule still holds for the saved-listing
  mutation only.

## Impact

- `src/JobHunt.Api/Features/Parsing/ParseMutations.cs`: one new mutation, reusing `ListingPageFetcher` and `RunJobAsync`.
- `schema.graphql` regenerated; `docs/STATUS.md` updated.
- Tests in `ParseTests.cs` via `ApiFactory` with the existing fake handler.
- No migration, no new package. UI calls the new mutation from the add-listing form (separate repo).
