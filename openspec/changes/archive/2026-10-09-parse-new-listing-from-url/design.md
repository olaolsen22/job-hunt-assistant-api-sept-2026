# Design

## Context

`ParseMutations.ParseListingFromUrlAsync` looks up the listing's URL, calls `ListingPageFetcher.GetListingTextAsync`
and then `RunJobAsync` in Full mode. The fetcher already enforces the public-address policy and throws
`InvalidParseRequestException` on unreadable pages. The add-listing form has a URL field but nothing saved yet.

## Goals / Non-Goals

**Goals:** parse from a typed URL during add, reusing the fetcher and job flow unchanged.

**Non-Goals:** auto-saving the listing, duplicate checks at parse time (`createListing` already does them), Quick or
Companies mode from URLs, caching fetched pages.

## Decisions

- **Separate mutation `parseUrl(url)` rather than an optional `url` on `parseListingFromUrl`.** One input per
  mutation keeps the "stored URL is the source of truth" rule intact for saved listings and avoids an
  either/or argument check.
- **Validate with `Uri.TryCreate(url, UriKind.Absolute, …)` and scheme `http`/`https`**, and a 2048-char cap (`Listing.Url` has no max length
  to reuse). Throw `InvalidParseRequestException` so the UI handles one error type it already knows.
- **Full mode only**, matching the saved-listing path; the owner is analyzing one ad.

## Risks / Trade-offs

- The client now chooses the URL → SSRF surface. Mitigated by the fetcher's existing per-connection address check
  (also applied on redirects); no new risk beyond what `Listing.Url` already allowed, since any saved URL was
  client-supplied too.
