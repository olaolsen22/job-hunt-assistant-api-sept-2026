# Design

## Context

`parseListing` builds a `ParseJob` from pasted text and runs `ListingParser` inline; the parser sends only
`job.RawText` (first 12k chars) and passes no tools to the model. Domain
errors are exceptions mapped to typed mutation errors (`InvalidParseRequestException`, `ListingNotFoundException`).
Tests swap the LLM for `FakeChatClient` through `ApiFactory`.

A real page checked while planning (finn.no/job/ad/477281376): 373 KB of HTML, a JSON-LD `JobPosting` wrapped under
a `"script:ld+json"` key, two `<main>` elements, and 11 related-listing `<article>` cards after the ad.

## Goals / Non-Goals

**Goals:**
- One button's worth of behavior: listing id in, the same `ParseJob` out, preview/edit flow untouched.
- Safe to point at arbitrary user-supplied URLs.
- Related listings on the page never reach the model when the page has structured data.

**Non-Goals:**
- Rendering JavaScript (headless browser), logging in to boards, or working around bot blocking. Such pages fail
  with "paste the text instead".
- Search-result or multi-listing URLs, and Quick/Companies from a URL.
- Background queueing: like `parseListing`, this runs inline until the queue lands.
- Per-site scrapers.

## Decisions

- **Fetch server-side, not Gemini's URL-context tool.** Many boards need JS or block bots, and the tool's failures
  are opaque and still cost a request. Our own fetch gives deterministic extraction, one place for the safety
  policy, and the extracted text stored on the job. Alternative: URL-context tool. Rejected for those reasons.
- **Separate mutation `parseListingFromUrl(listingId)`, not URL sniffing in `parseListing`.** The button lives on a
  saved listing, and the API is the source of truth for its URL: the mutation loads the stored `Url` (already
  validated as http(s) and normalized when the listing was saved) so the client can't send a stale or different one.
  Alternatives: a `url` argument (the client could disagree with the stored value, and a saved listing's own URL would
  always trip a duplicate check), or detecting a lone URL in `text` (a pasted ad that is only a link would silently
  change behavior). Both rejected.
- **No duplicate check.** The URL belongs to an existing listing, so a duplicate check would only ever find itself.
  Duplicates are `createListing`'s rule and stay there.
- **Typed error instead of a `FAILED` job for fetch failures.** A failed fetch has no model call to log and nothing
  for the owner to review; the mutation error already reaches the UI. `FAILED` jobs stay for LLM failures.
- **Safety at connect time.** One `HttpClient` built on `SocketsHttpHandler` with a `ConnectCallback` that resolves
  the host and refuses non-public addresses before connecting. Every redirect hop opens a connection through the
  callback, so redirects and DNS rebinding are covered without separate checks. Redirect cap 5, 2 MB body cap read
  from the stream (not trusting Content-Length), 10 s timeout, a browser-like User-Agent. Scheme check up front.
- **Extraction without a package first.** JSON-LD: pull `<script type="application/ld+json">` bodies with a
  regex, parse with `JsonDocument`, and search recursively for the object whose `@type` is `JobPosting` (handles
  the finn.no wrapper key, `@graph` and arrays). Description: strip tags with a regex and `WebUtility.HtmlDecode`.
  Fallback: drop `script`/`style`/`nav`/`footer`/`header`/`aside` blocks, strip tags, collapse whitespace.
  Alternative: AngleSharp. Held back per "framework built-ins first"; adopt it only if the regex fallback proves
  too fragile on pages the owner actually uses. The `<article>` card trick is deliberately not used: on other sites
  the ad itself is an `<article>`.
- **Hint inside `RawText`.** The fallback text starts with one line saying it was extracted from a web page and the
  first posting is the target. No new `ParseJob` column, so no migration, and the job records exactly what was sent.
  The JSON-LD path needs no hint because it contains only one posting.
- **Thinking for Full mode only.** `ChatOptions.Reasoning = Low` when `job.Mode == Full`, applied on the paste path
  too since both share the parser. Quick-add arrays (~30 rows) are the output-limit risk, so they stay without it.
  `MaxOutputTokens` stays 8,192, which is ample for one listing plus low-effort thinking.
- **Thinking tokens recorded in `OutputTokens`.** Gemini bills thoughts as output but reports them separately
  (`UsageDetails.ReasoningTokenCount`). Summing into the existing column keeps the cost estimate right with no
  schema change. Alternative: a new `ReasoningTokens` column. Rejected until the widget needs to show it.

## Risks / Trade-offs

- [Sites that block or JS-render (LinkedIn, some career pages)] → typed "paste the text instead" error; no guarantee
  per site is the stated non-goal.
- [Fallback extraction picks up related listings on pages without JSON-LD] → web-page hint in the input; the owner
  reviews the preview before saving anyway.
- [Regex HTML handling is fragile] → used only to find script blocks and strip tags, never to navigate structure;
  AngleSharp is the documented escape hatch.
- [SSRF through redirects or DNS tricks] → connect-time address check covers both; tests cover loopback, a private
  redirect target and a hostname resolving to a private address.
- [A fetched page is untrusted text going into a prompt] → it goes in the same `<posting>` block as pasted text, and
  the output is validated as JSON and reviewed in the UI before anything is saved.
- [`ChatOptions.Reasoning` may not map to Gemini's thinking config in Google.GenAI] → verify in the first task; if
  unmapped, set the thinking config through `RawRepresentationFactory` instead. Spec behavior is unchanged either
  way.
- [Whether a thinking-enabled call on the free tier counts toward the same daily limit] → one request either way;
  nothing to change.

## Open Questions

- Effort Low vs Medium: start at Low; revisit after a few real parses (a prompt/config tweak, not a spec change).
