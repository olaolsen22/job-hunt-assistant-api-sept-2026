# Tasks

## 1. Durable safety state

- [x] 1.1 Add AiOperation and UserLlmLimit entities/configurations, user filters/FKs/indexes, per-user request uniqueness, one-Running-operation partial unique index, nonnegative limit check, and same-owner parse-job relationships; verify temporary SQLite tests reject foreign references and concurrent active receipts while allowing the same requestId for different users.
- [x] 1.2 Add nullable operation/ordinal attribution to LlmUsage with same-owner FK, attempt uniqueness and personal-provider/day query index; generate AddAiCallSafeguards through make migration after backup and review Up; verify a temporary migration test preserves BYOK credentials, existing jobs, usage counts/tokens, and unset limits.
- [x] 1.3 Implement versioned canonical request/work digests protected with Data Protection and user-specific purposes; verify argument changes, model/default distinction, prompt/source snapshots, reconstruction across restart, and absence of plaintext CV/candidate/prompt content in receipts.
- [x] 1.4 Document receipt states, single-instance recovery, protected-digest key-ring dependency, and migration/backup implications in docs/llm-call-safeguards.md; verify documented fields and storage behavior against the model tests.

## 2. Personal-provider limit settings

- [x] 2.1 Add Owner-only setLlmCallLimit with supported provider validation and nullable nonnegative dailyRequestLimit; verify two-Owner isolation, Guest/anonymous denial, null/zero/positive settings, invalid inputs, and settings surviving credential replacement/removal.
- [x] 2.2 Add llmCallLimits using PacificDay/TimeProvider and personal user/provider consumption across models and historical credentials; verify null remaining for unset settings, floors at zero, daylight-saving/reset boundaries, and enabling/lowering limits after prior same-day calls without resetting usage.
- [x] 2.3 Publish settings/query examples and the shared-default-Gemini exemption in docs/llm-call-safeguards.md and docs/llm-provider-keys.md; verify fields and errors through GraphQL tests and clearly distinguish personal request admission limits from llmUsageToday's provider quota estimates.

## 3. Operation admission and duplicate behavior

- [x] 3.1 Add a shared operation coordinator with short atomic claim transactions, existing-ID comparison, typed admission errors, active-identity comparison, and terminal cleanup; verify parallel identical submissions cause one admitted operation, distinct overlapping work is ai-busy, and request conflicts make zero URL/provider calls.
- [x] 3.2 Add optional requestId inputs and route all six mutations through the coordinator while preserving validation/model allowlists and owner scoping; verify text modes, both URL paths, CV parsing, individual sections and full analysis, including duplicate calls with omitted or different IDs while active.
- [x] 3.3 Implement original owned ParseJob replay, transient result-unavailable responses, safe interrupted responses, and no silent identity expiry; verify completed lost-response replays make no fetch/inference/usage, changed default/key does not reroute an existing ID, and missing historical jobs cannot trigger replacement calls.
- [x] 3.4 Implement cancellation/final-save failure cleanup and startup recovery of Running receipts for one API instance; verify injected post-dispatch persistence failures and recreated hosts retain original identities, reject old-ID resubmission, release stale admission, and never auto-resume inference.
- [x] 3.5 Publish UUID-per-action/reuse-on-transport-retry guidance and machine-readable errors in docs/llm-call-safeguards.md; verify examples against mutation tests and explicitly state that sequential legacy requests or new IDs after completion can create fresh attempts.

## 4. Atomic inference admission and audit

- [x] 4.1 Refactor LlmExecution to revalidate/decrypt credentials before atomic allowance check plus durable usage reservation, using a dedicated context and short SQLite immediate transaction; verify last-allowance contention, zero/no-limit behavior, shared exemption, and no usage/allowance for local rejection or metadata validation.
- [x] 4.2 Update the same reservation after provider outcome and consumer decode validation, removing duplicate caller-owned usage inserts; verify one row per admitted ordinal, token attribution for all providers, safe pending/interrupted/canceled accounting, no fallback/retries, and caller content remaining unsaved on cancellation.
- [x] 4.3 Enforce limits against all personal-provider models and credential generations, including pre-migration BYOK rows; verify model switches/key rotation/limit edits cannot reset today's consumption and changing settings races safely with admission.
- [x] 4.4 Document admission counts and uncertain-delivery behavior in docs/llm-call-safeguards.md and docs/llm-provider-keys.md; verify usage and limit queries count an interrupted reservation conservatively while rejected and skipped work adds zero.

## 5. Batch stopping and partial results

- [x] 5.1 Add SKIPPED and safe errorCode to tailored results, preserving experience/education/skills/summary ordering and one operation admission across sections; verify existing successful review semantics and no profile/draft persistence.
- [x] 5.2 Stop analysis after invalid-key, credential-unavailable, rate-limited, timeout, provider-error, or daily-limit-reached and build skipped results without dispatch; update PersonalAiTests' two-rejected-call expectation and verify each stop code, retained earlier success, limit exhaustion partway through, and zero usage for skipped sections.
- [x] 5.3 Preserve continuation after section-specific invalid-json and cancellation propagation with no later calls; verify both cases with fake providers and audit counts.
- [x] 5.4 Update docs/tailored-cv-drafts.md with FAILED versus SKIPPED examples and partial-result handling; verify examples match batch tests and remain compatible with explicit review/save behavior.

## 6. Integration and review

- [x] 6.1 Refresh schema.graphql through the established schema verification flow and check cross-feature behavior with fake transports and temporary databases; verify anonymous/user isolation, legacy inputs, all six operation guards, personal settings, shared Gemini exemption, and durable restart/lost-response behavior together.
- [x] 6.2 Run targeted tests while implementing and the full dotnet test suite once after runtime edits; verify no real provider calls, no live database mutation, no owner API start/stop, and no redundant separate build when tests already build.
- [x] 6.3 Use log-progress for relevant docs/STATUS.md sections and record the owner-requested rationale; verify delivered schema/docs agree with code and mark tasks complete only after their checks pass. Reconcile add-user-provider-keys before this change's later spec sync/archive; do not archive either change as part of implementation without a request.
