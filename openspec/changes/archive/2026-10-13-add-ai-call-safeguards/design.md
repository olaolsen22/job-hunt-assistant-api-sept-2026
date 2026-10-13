# Design

## Context

See `proposal.md` for motivation. The current working tree implements `add-user-provider-keys`, although its delta specs have not yet been archived into main specs. That implemented provider boundary and its ownership/routing contracts are the baseline for this change; reconcile the planning changes in that order before archiving.

`LlmExecution.RunAsync` is the common inference boundary. It adds usage to the caller's context before inference but normally saves it only after the response. `ParseMutations` creates a fresh inline job per call; `ProfileCvParser` retains neither input nor candidate; `TailoredCvAnalyzer` runs one sequential request per section and saves usage after each section. Its existing personal-provider integration test deliberately expects two rejected calls for a two-section batch. Rate limiting in `Program.cs` applies only to auth. SQLite and the application are designed for one running API instance.

The user confirmed that users should choose their own limits and the default free Gemini path should be exempt. Daily request limits are the interpretation of the preceding daily-cap discussion, not dollar/token budgets. All shared-key inference is left outside personal limits; no new shared cap is introduced. Personal Gemini is subject to the Owner's setting even when its model is the configured default.

## Goals / Non-Goals

**Goals:** Establish durable local admission before inference; suppress accidental overlapping work across every consumer; preserve transient CV/analysis data; enforce opted-in limits without races; give the UI stable errors and partial batch results.

**Non-Goals:** Exactly-once execution at providers, replay storage of private candidates, multi-instance leases, automatic resume/retry, Redis, a background queue, cross-user caching, provider-wide quota claims, or monetary spend controls. A user can explicitly start a new request after a previous one finishes; this is not a permanent content cache.

## Decisions

### 1. Durable operation receipts with an additive GraphQL input

Add `requestId: UUID` to the six AI mutations. Generate a request UUID when omitted. Introduce `AiOperation` in an AI safeguards feature slice with a separate server-generated primary ID, RequestId, UserId, mutation kind, protected request digest, protected active-work digest, resolved provider/model/key attribution, Running/Succeeded/Failed/Interrupted state, safe error code, UTC timestamps, and optional ParseJobId. Use an AppUser FK, UserId index/filter, and unique `(UserId, RequestId)` identity. Separate primary IDs let different users safely submit the same requestId.

A partial unique UserId index for Running operations enforces one active top-level operation at the database boundary. The optional parse-job relationship must enforce the same owner through a composite FK/principal key; apply equivalent owner integrity to operation/usage relationships. DTOs, not inferred entity fields, expose any operation metadata. No receipt cleanup or automatic identity expiry is introduced: deleting identities would allow a historical ID to buy a second attempt.

Use versioned canonical argument serialization and SHA-256 digests, protected with existing Data Protection and a user-specific purpose before storage. The request digest covers mutation kind and all submitted arguments except requestId, retaining the distinction between omitted/default model selection and explicit selection. Identical requestId arguments can replay despite later default/key changes. For active-work matching, include the effective provider/model/credential and source snapshot used to build the prompt. This distinguishes changed profile/listing context without persisting it. Compare decrypted digests for the one current user's active row, so randomized ciphertext needs no equality index and no separate HMAC secret is required.

For direct text mutations use the parser's existing text normalization; URL identity uses the validated submitted URL or saved listing ID and current URL. Include mode/section/source-entry ID and prompt version where relevant. For profile-based analysis hash the allowlisted input snapshot before provider admission. Claim the top-level operation before any URL fetch; after fetch complete the inference fingerprint from the actual extracted input. Never persist raw CV/analysis input or results.

Alternatives: an in-memory semaphore loses retry identity on restart; caching candidate results changes the privacy contract; requiring requestId immediately breaks legacy UI clients. Protected digests plus one active row keep durable state small while avoiding persistent raw argument hashes.

### 2. Separate operation admission from per-attempt admission

Order new operations as authentication/local validation, existing receipt lookup when an ID is supplied, current allowed-model/source validation, and short database operation admission. No outbound fetch occurs before admission. If a supplied ID exists, compare submitted arguments first: conflicts produce `request-conflict`; Running produces `operation-in-progress`; completed listing operations load the original owned ParseJob; completed transient operations produce `result-unavailable`; Interrupted produces `operation-interrupted`. A deleted or unavailable historical parse job also yields `result-unavailable`, never a fresh call.

If another Running operation exists, compare its submitted request digest and effective selection first; identical submissions produce `operation-in-progress` even before URL extraction finishes. Use the protected work digest when available to recognize equivalent effective inputs; other work produces `ai-busy`. In particular, concurrent requests with different IDs are rejected rather than attached to another receipt. No operation is queued. Analyze-all owns one admission across its entire loop; its sections do not acquire top-level admission again.

Finish the receipt and save the listing job reference in the same transaction as normal job completion. On caller cancellation mark the operation Interrupted through a separate context and rethrow cancellation. A coordinator finally path closes any uncompleted admitted operation through an independent context without the canceled request token, including after result-save failure. If the database itself is unavailable this remains fail-closed until recovery. Recovery at single-instance startup marks leftover Running receipts Interrupted and clears their active status before serving requests. Original identities remain immutable. Failure before sending any provider attempt still closes the operation with a safe outcome and releases concurrency; a new explicit requestId is needed to try again. Enforce all paths through one operation coordinator and the existing execution boundary, not scattered mutation-specific locks.

Alternatives: automatically retrying stale Running work can duplicate an already-billed attempt; holding a SQL transaction over the network blocks unrelated work. Short atomic admissions and conservative interruption recovery match the current single-instance architecture.

### 3. Persist usage reservation before dispatch

Add nullable operation attribution and attempt ordinal to `LlmUsage`, allowing historical rows to remain unchanged. Enforce one `(AiOperationId, AttemptOrdinal)` for attributed rows, owner integrity, and indexes for personal `(UserId, Provider, KeySource, CreatedAt)` limit queries. Each section has a deterministic ordinal inside its admitted operation; a second reservation of that ordinal is rejected rather than resent.

After validating/re-reading/decrypting the selected credential, use a short SQLite immediate write transaction on a dedicated context to check the user's personal-provider limit and insert a usage row with `Success=false`, `ErrorCode=attempt-pending`, timestamps, and actual credential attribution. Commit before invoking the provider. This durable row is both the allowance reservation and audit record; do not create a second caller-owned usage row. Update the same row after the response/failure/cancellation and after decode validation. Preserve token normalization and operation names. Caller-owned profile/jobs/content must never be saved by this audit context.

Count all personal rows in that user's provider-day, including historical BYOK rows without operation attribution. That prevents enabling a setting from resetting today's consumption. Quota check plus reservation must run under the same immediate transaction: a count-then-save without write serialization is insufficient. Do not hold it across the HTTP call. Serialize limit-setting changes similarly so admissions and updates have a defined commit order. Translate busy/constraint admission failures to safe local errors; never retry inference to resolve SQL contention.

At startup, change stale `attempt-pending` usage on interrupted operations to a safe interrupted code. If completion persistence fails after a provider response, the pending row remains counted and the operation cannot be retried under the same ID. A reservation can survive a crash just before dispatch even though no billable call occurred; describe this conservative uncertainty rather than promising exact provider counts. Local credential-decryption/input/limit rejection occurs before reservation and creates no usage. Metadata key validation is outside inference accounting.

Alternatives: count completed usage after a response allows simultaneous admissions through a cap; a separate daily counter table duplicates information already in indexed usage; refunding uncertain attempts can undercount actual provider submissions.

### 4. User-managed daily limits, independent of provider quotas

Add `UserLlmLimit` with UserId, supported provider, nullable DailyRequestLimit, and UpdatedAt, unique `(UserId, Provider)`, AppUser FK, UserId filter/index, and nonnegative check. Owner-only `setLlmCallLimit(provider, dailyRequestLimit)` returns the updated safe setting; null clears enforcement, zero pauses personal inference, negative values/unknown providers are typed invalid-input errors. Settings can be configured before a personal key exists and survive its rotation/removal. No API accepts another user's ID.

`llmCallLimits` returns the Owner's supported providers with dailyRequestLimit, requestsConsumed, remainingRequests, and resetsAt; non-Owners receive no personal settings. Missing settings mean null. Consume by user/provider across models and credential generations, not by credential ID. Use `PacificDay` and TimeProvider for queries/admission. Queries make no provider calls. Do not repurpose `llmUsageToday.requestsLimit`, which remains a shared-model quota estimate; the new query reports application admission limits.

Apply settings only when resolved KeySource is User. All shared Gemini inference remains uncapped by this feature. Duplicate/concurrency admission and no retries/fallback still protect shared/free operations. Enabling a cap checks already-admitted attempts today; lowering below consumption blocks future calls without rewriting history.

Alternatives: per-credential limits can be bypassed by rotation; caps imposed by the server contradict the user's preference; dollar caps require pricing coverage and provider billing semantics absent from this scope.

### 5. Batch failures expose skipped sections

Preserve section order: experiences, education entries, skills, summary. Add `SKIPPED` to `TailoredCvAnalysisStatus` and nullable safe `errorCode` to its result. Carry codes from `LlmExecution` without parsing messages. Stop after invalid-key, credential-unavailable, rate-limited, timeout, or provider-error. The attempted section is Failed; all remaining section descriptors become Skipped with the stop code and no attempt reservation or inference. A daily-limit rejection before dispatch makes that section and all remaining sections Skipped. Invalid-json is section-specific and does not stop later work. Preserve cancellation propagation rather than converting it into a successful partial report.

Keep previously completed suggestions in the current response, with no draft/profile writes and no durable suggestion cache. Update the existing test expecting two unauthorized section calls to expect one failed attempt and one skipped result. Unexpected database/persistence errors fail the operation safely; they must not permit continuation or automatic restart of the batch.

Alternatives: stopping on every invalid-json result discards useful independent work; continuing after systemic rejection repeats doomed requests. Typed codes make the distinction reliable.

## Risks / Trade-offs

- Optional requestId does not protect a sequential legacy retry after completion -> document that clients generate one UUID per user action and reuse it only for transport retries; active guards protect older clients while running.
- A different requestId can still deliberately start new paid work after completion -> enforce chosen daily limits, avoid automatic client retry with new IDs, and state this boundary plainly.
- Lost CV/analysis results cannot be replayed -> return result-unavailable and require explicit user action for a new attempt rather than retaining sensitive candidates.
- Restart or final-save failure can leave conservative consumed attempts -> persist admission first, recover to Interrupted, and expose safe accounting semantics.
- One operation per user reduces automation throughput -> fits the personal app and prevents overlapping analyze-all work; no queue is added.
- Single-instance recovery would be unsafe with multiple API processes -> keep the established single-instance deployment constraint; distributed leases are outside scope.
- New SKIPPED enum/errors require UI handling -> publish GraphQL examples and schema changes; frontend implementation lives in the separate UI repository.
- Provider-wide spending is invisible to this application -> report application request limits only, never provider credit/billing guarantees.

## Migration Plan

1. Implement against the reviewed BYOK working tree; complete/archive its specs before this change's spec sync/archive so credential baseline requirements are preserved.
2. Use `make migration name=AddAiCallSafeguards` after its backup step; review Up for preservation of real data. Add receipt/limit tables, owner-integrity keys, nullable usage attribution/indexes. Do not reset historical usage or delete credentials/jobs.
3. Test migration and concurrency with temporary SQLite files and fake transports. Do not start/restart the owner's API or apply migrations to its live database during implementation.
4. Publish schema/API guides and deploy the migration with receipt recovery. Existing inputs remain accepted; no limits become enabled automatically.
5. Rollback needs coordinated downtime and a compatible database backup. Restoring an earlier backup loses later receipt identities and can make old requests repeatable; require clients to reconcile interrupted actions instead of silently retrying them. Preserve the matching Data Protection key ring for both credentials and protected receipt digests.
