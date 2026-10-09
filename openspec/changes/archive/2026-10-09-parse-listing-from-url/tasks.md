# Tasks

## 1. Thinking mode for full parses

- [x] 1.1 Set `ChatOptions.Reasoning` to low effort for `ParseMode.Full` in `ListingParser`; if Google.GenAI doesn't map it, use `RawRepresentationFactory` with a thinking config. Verify with a `FakeChatClient` test that captures the options: Full has the setting, Quick and Companies don't
- [x] 1.2 Record `OutputTokens` as output plus `ReasoningTokenCount` in the usage row. Verify with a test whose fake returns both counts and the row holds the sum
- [x] 1.3 Note the thinking setting under the parser in `docs/STATUS.md`; verify the text matches the code

## 2. Page fetching and extraction

- [x] 2.1 Add the page fetcher (`Features/Parsing`): `HttpClient` on a `SocketsHttpHandler` with a `ConnectCallback` that refuses loopback/private/link-local addresses, redirect cap, 2 MB stream cap, 10 s timeout, browser-like User-Agent; register it in `Program.cs`. Verify with tests on a fake handler or local listener: loopback refused, redirect to private refused, oversized body and timeout produce the "can't read" failure
- [x] 2.2 Add extraction: JSON-LD `JobPosting` search (wrapper keys, `@graph`, arrays), description tag-strip plus entity decode, and the stripped-text fallback with the web-page hint line. Verify with unit tests on a trimmed copy of the finn.no page as fixture: only the ad's text comes out, no related-listing text, `&#43;` becomes `+`; a page without JSON-LD gets the hint; an empty shell reports nothing readable

## 3. The mutation

- [x] 3.1 Add `ParseListingFromUrl(listingId)` in `ParseMutations`: load the listing's `Url` (own listings only, else `ListingNotFoundException`), fetch and extract, then create the job and run `ListingParser` exactly as `ParseListingAsync` does. Verify with `ApiFactory` tests: success logs one usage row and stores the extracted text; an unknown id, another user's listing, a 403 page and an empty page each return their typed error with no job or usage row
- [x] 3.2 Regenerate `schema.graphql` and update `docs/STATUS.md` (mutation, errors, fetch limits); verify the schema diff shows only the new mutation and its error types

## 4. Integration check

- [x] 4.1 With the owner's go-ahead, run the real finn.no URL from the plan against a test port and DB, and confirm the preview JSON has the right title and company and none of the related listings. Skip if the owner declines a live probe; `dotnet test` stays the gate
