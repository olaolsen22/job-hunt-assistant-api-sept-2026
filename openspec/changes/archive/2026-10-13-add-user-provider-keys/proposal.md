# Proposal

## Why

Real users need to run AI features with their own provider credentials while keeping the configured shared Gemini key available when no personal Gemini key is configured. Usage must identify the credential that actually served each call so personal activity does not consume the shared-key usage totals or expose another user's activity.

## What Changes

- Add authenticated, write-only key management for Gemini, Anthropic, OpenAI, and Mistral, with provider validation before saving, encrypted storage, and safe status queries. Only Owners may manage personal keys; Guests retain shared Gemini access.
- Keep one active key per user/provider; replacing or removing it preserves usage attribution to the previous credential without retaining its secret.
- Persist the Data Protection key ring across container recreation and document development and backup/restore configuration.
- Extend the configured model catalog and resolver to return provider-aware choices and use the current user's personal key where available. A personal-key failure returns a safe error; it never triggers another provider or the shared key automatically.
- Route every built-in AI operation through a common provider boundary while preserving existing prompts, review flows, and result validation.
- Report usage per credential and model. Shared Gemini totals still include all shared-key users; personal totals belong only to the key's owner. Historical rows remain shared Gemini usage.
- **BREAKING behavior:** daily usage history excludes other users' personal activity and separates credential/provider/model groups; personal-key selections have no inferred free-tier quota. Add credential/provider metadata and selectors to usage responses.
- Publish the additive GraphQL contract and API integration guidance so the separate UI can let users switch models or remove a failing key explicitly.

## Capabilities

### New Capabilities

- `llm-provider-keys`: Private provider credential management, validation, durable encrypted storage, routing, and safe provider errors.

### Modified Capabilities

- `llm-model-selection`: Provider-aware configured choices, personal-key availability, default resolution, and credential-aware execution of all AI operations.
- `llm-usage`: Usage attribution and visibility per credential, provider, and model, with shared-only quota estimates.
- `listing-parsing`: Provider-neutral failure messages and credential attribution for parsing calls.

## Impact

Backend only: `Features/LlmModels`, new key management slice, `Llm`, usage queries, all three parser/analyzer services, `ParseMutations`, EF configurations/query filters/migration, startup/DI, public configuration, Docker key-ring persistence, tests, `schema.graphql`, and API documentation. New provider SDK/adapters are centrally pinned. The Nuxt UI, multiple simultaneously active keys per provider, custom endpoints, provider billing synchronization, application quota enforcement, background queues, Ollama, and automatic failover are outside this change.
