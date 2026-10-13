# Proposal

## Why

AI credentials are protected, but duplicate requests can still trigger repeated paid calls and full-profile analysis keeps calling after credential or rate-limit failures. Users need backend protection against accidental repeats and an optional limit they control for personal-key usage.

## What Changes

- Add optional UUID `requestId` inputs to all six built-in AI mutations, durable per-user operation receipts, and rejection of overlapping identical work. Reusing an ID must never silently launch another attempt; listing results can reuse their existing parse job while CV/analysis results remain transient.
- Limit each user to one active top-level AI operation, rejecting overlapping operations instead of queuing paid work. Section calls inside one analyze-all operation share that operation admission.
- Let Owners set, change, or disable a daily request limit per personal-key provider. Limits start disabled, aggregate across models and credential replacements, and count each admitted inference attempt atomically. Shared-key inference, including the default free Gemini model, has no user-configured daily cap in this change.
- Keep duplicate/concurrency guards and failure stopping active for free shared Gemini. Existing configured Gemini quota figures remain informational.
- Stop analyze-all after credential rejection/unavailability, provider rate limiting, timeout, provider unavailability, or application limit rejection. Preserve completed results and return explicit skipped sections without extra provider calls or usage rows.
- Publish safe machine-readable admission errors and a query showing the user's configured limits, consumed attempts, and reset time.
- **Behavior change:** duplicate/busy/limit-rejected requests can return typed errors before inference; analyze-all can return `SKIPPED` sections. Legacy callers may omit `requestId`, but only callers that reuse it receive durable retry protection after completion.

## Capabilities

### New Capabilities

- `llm-call-safety`: Operation identity, duplicate/concurrency admission, durable attempt receipts, safe recovery, and preservation of transient CV/analysis data.
- `llm-call-limits`: User-controlled daily request limits for personal providers, atomic enforcement, and private settings/consumption reporting.

### Modified Capabilities

- `tailored-cv-drafts`: Full-profile analysis stops on systemic failures and returns skipped sections while preserving completed suggestions.

## Impact

Backend only: AI mutation inputs, shared execution boundary, parser/analyzer orchestration, new EF operation/limit state and migration, GraphQL schema, fake-provider tests, and API guides. Build on the currently implemented `add-user-provider-keys` change; preserve credential protection, routing, no retry/fallback, ownership, and existing usage attribution. No frontend implementation, background queue, cross-user content cache, monetary budget enforcement, provider billing integration, automatic retries, or key-ring hardening is included.
