# Design

## Context

`ListingParser.RunAsync` catches `HttpRequestException` (Google's `ApiException` derives from it) and maps 429 to
`rate-limited`, everything else to `provider-error`. The caller (`ParseMutations`) saves the job and usage row after
`RunAsync` returns, so any exception escaping `RunAsync` loses both. Google.GenAI's package docs mention an HTTP
timeout option but not how a timeout surfaces; the SDK calls the API over `HttpClient`, which throws
`TaskCanceledException` on timeout. This was not observed against the real API. Requirements are in
`specs/listing-parsing`.

## Goals / Non-Goals

**Goals:**
- A timeout is a normal FAILED outcome with a usage row.

**Non-Goals:**
- Retries or backoff (the parse queue change).
- Configuring the SDK's timeout value.
- Logging usage when the user's own request is cancelled (client navigated away); there is no job to report to.

## Decisions

**Catch `TaskCanceledException` only when our token isn't cancelled.**
`catch (TaskCanceledException) when (!ct.IsCancellationRequested)`: a timeout from `HttpClient`, not the client
hanging up. Provider-agnostic, so it also holds for Ollama later (OllamaSharp uses `HttpClient` too).
- Catch all `OperationCanceledException`: would swallow real request cancellation.
- Wrap the call in our own `CancelAfter`: adds a second timeout to reason about.

**New error code `timeout`**, separate from `provider-error`, so usage stats can tell a slow model from a down one.

**Docs rewrite targets only stale sections.** PLAN §4, §7, §9 and a new "Deferred follow-ups" list; other sections still
match the code. STATUS gets the header fix only; content is current.

## Risks / Trade-offs

- [The SDK surfaces timeouts differently than assumed] → the test pins our handling with the fake client; if a real
  timeout still escapes, the log shows its type and the catch is adjusted.

## Open Questions

- Which exception the real Google.GenAI client throws on timeout; confirm the first time one happens in the logs.
