# Tasks

## 1. Credential storage and durable encryption

- [x] 1.1 Add `UserProviderKey` entity/configuration, AppUser relationship, owner query filter, immutable credential IDs, retired-secret nullability, and one-active-key partial unique index; verify model tests enforce ownership and active-key uniqueness while permitting retired history.
- [x] 1.2 Add usage key source/credential FK and credential-owner integrity plus query indexes; generate `AddUserProviderKeys` with `make migration name=AddUserProviderKeys` after backup and review `Up()`; verify migration tests preserve historical counts/models/tokens as Shared and reject foreign credential attribution using temporary databases.
- [x] 1.3 Configure stable Data Protection application/purposes and persistent configurable key-ring locations, including `/data/dp-keys` for Docker; verify protection round trips across reconstructed providers, wrong-owner/provider/credential rejection, and safe missing-key-ring failure without calls to shared Gemini.
- [x] 1.4 Document encrypted credential/key-ring storage, sensitive backup/restore pairing, development directory configuration, directory permissions, and rollout cookie effects in `docs/llm-provider-keys.md`; verify documented paths and config names match implementation and existing Docker volume wiring.

## 2. Provider boundary and key management

- [x] 2.1 Pin compatible OpenAI/Microsoft.Extensions.AI.OpenAI and official Anthropic packages in centralized dependency files, retaining Google.GenAI and using Mistral's fixed compatible endpoint; verify dependencies restore and each provider adapter constructs through fake transports without real credentials.
- [x] 2.2 Implement fixed-endpoint authenticated model-list validation with local provider/key bounds, 10-second timeout, cancellation, disabled retries, and safe errors; verify fake-transport cases for success, blank/oversized/unsupported input, 401/403, 429, network failure, and timeout without inference usage rows.
- [x] 2.3 Implement Owner-only `setProviderKey`/`removeProviderKey` and safe `providerKeys(includeRetired)` DTOs, with transactional replacement/retirement and erased ciphertext; verify GraphQL tests for two-Owner isolation, Guest/anonymous denial, failed replacement preservation, repeated removal, concurrent active-key protection, and no secret/ciphertext fields.
- [x] 2.4 Implement the shared/personal client execution boundary with explicit lifetime ownership, reusable HTTP transports, disabled automatic inference retries, bounded timeout, provider-neutral safe error mapping, and caller cancellation; verify fake transport/logging tests show no secret/header/body/text leakage and no shared-client disposal or cross-user authorization reuse.
- [x] 2.5 Add provider-specific chat option and token normalization; verify mapped native model IDs, JSON/output options and reasoning handling for all four providers, and that already-inclusive reasoning/output token counts are not double-counted.
- [x] 2.6 Document key-management payloads, supported providers, last-four status/history, metadata validation limitations, and stable errors in `docs/llm-provider-keys.md`; verify examples against the generated GraphQL schema and tests from this group.

## 3. Model catalog and defaults

- [x] 3.1 Add validated server-owned Anthropic/OpenAI/Mistral model catalogs with at least one configured supported text model per provider and collision-free qualified selection IDs, preserving native Gemini IDs; verify catalog tests cover duplicates, malformed entries, and output/context settings compatible with existing parsing budgets.
- [x] 3.2 Extend asynchronous allowed-model/default/explicit-selection resolution to return provider, native model, source, and safe credential ID; verify Owner/personal-key availability, Guest/non-Owner shared-default restriction, exact ID matching, personal Gemini preference, absent-key shared Gemini routing, and explicit unavailable-provider rejection.
- [x] 3.3 Extend `llmModels` DTOs and default mutations to persist the selected provider and selection ID; verify existing Gemini query/mutation shapes remain usable, cross-user defaults stay isolated, and removed catalog/key defaults resolve to configured Gemini.
- [x] 3.4 Document the model selection ID/native model distinction, catalog config, default resolution, and explicit UI switching/removal flow in `docs/llm-provider-keys.md`; verify existing Gemini examples still work and new qualified IDs appear consistently in the schema/tests.

## 4. Route all built-in AI operations

- [x] 4.1 Route listing parsing (FULL/QUICK/COMPANIES and both URL mutations) through resolved provider/credential execution, retaining pre-fetch selection checks and safe job failures; verify all paths preserve result shapes, record actual provider/native model/credential, and make zero fallback calls on personal-key errors.
- [x] 4.2 Route CV parsing through the same boundary and attribute its usage; verify complete accepted input reaches the selected provider, invalid input/selection makes no call, failure returns the existing failed result, and raw CV text is never persisted or logged.
- [x] 4.3 Route tailored section and full-profile analysis through the same boundary, preserving iteration and review semantics; verify each attempted section records the actual credential, source IDs remain valid, personal failure cannot switch provider/key, and no drafts/profile content are saved implicitly.
- [x] 4.4 Update existing API guides for parsing/CV/tailored operations with provider-neutral failures and key routing; verify documentation matches each operation's returned result/job and error behavior tested in this group.

## 5. Credential-aware usage reporting

- [x] 5.1 Scope `llmUsageToday` to effective credential/provider/model, exposing safe attribution and shared-only quota limits; verify same-model calls from two personal Owners and shared Guest remain separated, failed/null-token calls count correctly, Pacific reset boundaries remain correct, and personal limits are null.
- [x] 5.2 Add optional credential filtering and attribution grouping to `usageDaily`, including retired personal history and shared aggregates, with provider/model price lookup and legacy Gemini-price fallback; verify foreign-ID rejection, rotation-history preservation, model-name collisions, missing prices, day bounds, and exclusion of other users' personal usage.
- [x] 5.3 Document usage response fields, shared aggregate visibility, private credential history, price configuration, reporting-day semantics, and migration from model-only grouping in `docs/llm-provider-keys.md`; verify examples match tested query output and existing shared-only historical results.

## 6. Integration and handoff

- [x] 6.1 Refresh `schema.graphql` through the established schema verification flow and reconcile examples across affected API guides; verify schema checks and existing Gemini clients' selections remain valid alongside additive key/model/usage fields.
- [x] 6.2 Run targeted feature/migration/ownership tests during implementation and the full `dotnet test` suite once after runtime edits; verify shared fallback, no personal-key failover, all four provider request mappings, and usage privacy without live providers, real database mutations, or starting/stopping the owner's API.
- [x] 6.3 Use `log-progress` to update only relevant STATUS behavior/config/entity sections and record the BYOK rationale in `.claude/commit-notes.md`; verify the documented delivered contract agrees with code, schema, tests, and all completed change tasks.
