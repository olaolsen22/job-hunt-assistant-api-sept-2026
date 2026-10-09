# Proposal

## Why

Deep analysis of a target listing today means opening the ad, copying its text and pasting it into the UI. A "Parse
automatically" button on a saved listing should do that from the URL the listing already stores: the API fetches the
page, extracts the one posting on it and returns the same parse result the paste path returns, so the owner still
reviews and edits the preview before saving. The model can't fetch the page itself (the parser sends it only pasted
text and passes no tools), so the API has to. The API is the source of truth for the URL, so the client sends only the
listing id.

## What Changes

- New GraphQL mutation `parseListingFromUrl(listingId)`. It reads the listing's stored URL (own listings only) and
  parses it in Full mode; Quick and Companies are unaffected. It returns the same `ParseJob` as `parseListing`, so the
  UI's preview/edit step is unchanged.
- New server-side page fetch with a safety policy: no private/loopback/link-local targets (checked again on every
  redirect), capped redirects, response size and timeout.
- Text extraction in order of preference: the page's JSON-LD `JobPosting` (title, company, location, dates, and the
  description with tags stripped and entities decoded), else the page text with non-content elements removed. The
  fallback tells the model the text came from a web page and the first posting is the target, because boards append
  related listings (finn.no puts 11 cards after the ad).
- Expected failures become typed mutation errors, with no job created: listing not found, and page not readable
  (non-200, timeout, too large, blocked address, no usable text).
- Thinking mode enabled for Full-mode parses on both the paste and URL paths (low effort), with usage logging counting
  thinking tokens.

## Capabilities

### New Capabilities
- `listing-url-parsing`: parsing a saved listing from its stored URL: fetch policy, extraction and error behavior,
  plus thinking for full-mode parses.

### Modified Capabilities
None. `openspec/specs/` has no archived specs yet, so the thinking-mode requirement lives in the new spec for now.

## Impact

- `src/JobHunt.Api/Features/Parsing/`: new `ParseListingFromUrl` mutation and a page-fetching/extraction class;
  `ListingParser` gains a thinking option and takes the text it is given unchanged.
- `src/JobHunt.Api/Program.cs`: an `HttpClient` registration for the fetcher.
- `schema.graphql` regenerated; `docs/STATUS.md` updated.
- Tests: fetcher tests with a fake `HttpMessageHandler`, mutation tests via `ApiFactory`.
- No migration. No new package unless the fallback extraction proves too brittle with regex (see design.md).
- UI: one new mutation to call (separate repo, not edited here).
