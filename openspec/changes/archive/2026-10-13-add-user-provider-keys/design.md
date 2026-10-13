# Design

## Context

See `proposal.md` for motivation and scope. The application currently registers one keyed singleton Gemini `IChatClient`; `ListingParser`, `ProfileCvParser`, and `TailoredCvAnalyzer` consume it directly. Listing mutations resolve models before fetching URLs and creating jobs. CV parsing and tailored analysis resolve inside their services. `LlmModelResolver` returns Gemini-only options and restricts non-Owners to the configured default.

`UserLlmSetting` already has a provider column but mutations hardcode Gemini. `LlmUsage` stores actor/provider/model; both usage queries ignore user filters and group only by model. Prices are model-keyed. The existing specs deliberately require shared totals, so this change modifies that contract rather than treating it as a query optimization.

The Docker volume already mounts `/data`; there is no explicit persistent Data Protection configuration. Tests substitute a fake shared chat client; new provider boundaries need fake transports and multi-user integration coverage without real keys or inference calls.

## Goals / Non-Goals

**Goals:** Resolve a complete provider/model/credential selection before inference; keep secrets inside the provider boundary; preserve attribution after credential replacement; reuse the existing GraphQL model parameter and result shapes where possible.

**Non-Goals:** A provider registry plugin framework, arbitrary endpoint support, application-side billing enforcement, automatic retries/failover, or a new background execution architecture. The existing shared config credential is a single logical credential (`shared-gemini`); server-key rotation history is not introduced here.

## Decisions

### 1. One active credential per user/provider with immutable history

Add `UserProviderKey` in a `Features/LlmProviderKeys` slice: UUID ID, UserId, provider, encrypted secret nullable, Last4, CreatedAt, RetiredAt nullable. Configure the AppUser FK and global UserId query filter. A partial unique `(UserId, Provider)` index for non-retired rows prevents two active credentials. Use UTC timestamps and existing enum/string conventions.

Validate remotely before starting the write transaction. Then retire the existing record, erase its ciphertext, and insert a fresh credential atomically. Removal retires and erases; already-unconfigured removal succeeds. Failed validation leaves the old record untouched. Database conflicts return safe typed errors. An already-started call may finish with the credential it captured; subsequent calls must observe retirement.

Add `KeySource` (Shared/User) and nullable `UserProviderKeyId` to `LlmUsage`. Personal usage references the credential with a real FK that cannot cascade-delete usage on retirement; enforce matching key owner and usage actor through a composite credential/UserId FK. Shared rows have no personal FK and expose `shared-gemini` as the safe public credential ID. Add indexes for the actual credential/provider/model/date queries. Do not expose EF credential entities through inferred GraphQL fields; return explicit metadata DTOs.

Alternatives: overwriting one ciphertext row would merge old/new keys' usage; deleting credential rows would lose attribution; multiple active keys require additional selection policy. The bounded credential-history records solve replacement without retaining old secrets.

### 2. Write-only management and bounded metadata validation

Expose `providerKeys(includeRetired: false)`, `setProviderKey(provider, apiKey)`, and `removeProviderKey(provider)` through GraphQL. Owner authorization is affirmative, preserving the current least-privilege treatment of new roles. Queries return only safe DTO fields, and unconfigured status entries for supported providers. Retired metadata is opt-in for history labels.

Use authenticated model-list requests at fixed official HTTPS endpoints, one bounded request (10 seconds, no automatic retries), with no inference prompt or token usage. Validate provider and 1–4,096-character trimmed secret locally first. Categorize 401/403 as invalid credential, 429 as rate-limited, timeout separately, and other failures as provider-error. Avoid raw response bodies in messages/logs. A metadata success validates authentication, not future inference permissions or credit. No provider/model-list payload is used as the application's allowlist.

Alternatives: a paid inference probe would add cost and usage solely for saving settings; checking key syntax alone does not establish authentication. Model-list validation is deliberately narrow and its limitations are documented.

### 3. Data Protection with explicit persistence and purpose binding

Protect ciphertext with an application-specific versioned purpose including UserId, provider, and credential ID. Keep a stable application name so container recreation uses the same protection scope. Configure a non-secret key-ring path: `/data/dp-keys` in Docker and an explicit persistent development location. Do not store the key ring in source control or recreate it at startup.

Persisting a key ring does not itself encrypt its files. Protect directory access for the runtime user, document that a database plus an unprotected key ring enables decryption, and back up/restore both as sensitive material. External certificate/key-vault wrapping is optional deployment hardening, not a new service dependency. Missing/invalid key-ring data causes a safe `credential-unavailable` failure with no fallback or inference call. Document the effect of the stable application name on preexisting login cookies at rollout.

Alternatives: plaintext storage violates credential privacy; custom encryption introduces key-management code; Data Protection already integrates with this ASP.NET application.

### 4. Provider-aware catalog with compatible selection IDs

Keep `Llm:Gemini` and its shared key/default/models configuration. Add server-configured catalogs for Anthropic, OpenAI, and Mistral (model IDs and labels, at least one supported text model per provider). Select models whose context/output capabilities support the current accepted CV text and up to 32,768 output tokens. Validate unique selection IDs, required labels, and configured limits before accepting calls.

Preserve existing Gemini model IDs as selection IDs. Other selections use `provider:native-model-id`; reject catalog collisions at startup. Add Provider, ModelId (native), KeySource, and CredentialId metadata to model DTOs. The resolver loads current user's active credentials asynchronously and resolves a server-owned record containing selection ID, provider, native model, and credential reference. Never pass the qualified selection ID to a provider SDK.

Owners may use all configured Gemini models and other providers only with their own active keys. Non-Owners keep only shared Gemini's configured default. A Gemini choice automatically uses the Owner's personal Gemini key when present, otherwise shared. An Anthropic/OpenAI/Mistral choice never maps to Gemini implicitly. Store the selected provider in `UserLlmSetting.Provider`; retain its default selection ID. A stale stored choice resolves to configured Gemini; an explicitly requested unavailable choice is rejected before fetch/job/usage, as today.

The UI can switch to another available provider/model, or remove the failing Gemini key to use shared Gemini. This change does not add a simultaneous shared/personal Gemini credential picker. Current Gemini callers continue using their existing IDs.

Alternatives: accepting arbitrary native model strings bypasses server policy; changing every Gemini ID would break clients; adding provider parameters to every existing mutation complicates the otherwise reusable picker contract.

### 5. One inference boundary with provider-specific transport behavior

Use a small shared `LlmClientFactory`/execution boundary for the three consumers and key validation, retaining `IChatClient` for inference. Preserve the DI-owned shared Gemini singleton. Personal client wrappers are scoped to a call (or the bounded multi-section operation), use factory-managed reusable HTTP transports, and are disposed without disposing the shared singleton or transport pools. Do not mutate shared authorization headers or cache secrets in singleton user-client dictionaries.

Provider mapping: existing Google.GenAI adapter for Gemini; `Microsoft.Extensions.AI.OpenAI` with the OpenAI SDK for OpenAI and Mistral's fixed compatible endpoint; official `Anthropic` SDK's own `AsIChatClient` for Anthropic. Pin versions compatible with the existing Microsoft.Extensions.AI dependency and .NET 10 in `Directory.Packages.props`; do not assume `Microsoft.Extensions.AI.Anthropic` exists. Tests exercise provider-specific request mapping through fake transports.

Normalize provider exceptions at this boundary, because SDK error types differ. Safe internal failures carry only a stable code and sanitized message. Preserve caller cancellation, apply an explicit inference timeout, and disable SDK automatic inference retries so one application attempt is one accounted provider attempt. Authentication failures are `invalid-key`; decryption failures are `credential-unavailable`; neither triggers fallback. Use provider-neutral rate-limit messages instead of claiming a personal key is free-tier.

Reuse operation-specific prompts and decoders. Set native ModelId, translate JSON/output/reasoning options to what each adapter supports, and never send Gemini-specific options blindly. Instructions plus existing strict output validation remain required even with native JSON mode. Normalize usage at the provider boundary: reasoning tokens are added only when provider output counts exclude them; already-inclusive output must not be counted twice. Preserve all accepted CV input, caller review semantics, and transient raw CV handling.

Resolve attribution once at each inference attempt; use it both for the SDK and logging. `ParseJob.Provider/Model` remain actual provider/native model, while no secret is persisted. Keep unknown-selection validation before URL fetches. Preserve tailored section iteration/result semantics; each attempted section has an attributed usage row. Safe pre-inference credential failures return existing failed-result/job shapes without pretending an inference request occurred.

Alternatives: replacing only the singleton registration would still leave call sites Gemini-bound; constructing HTTP connection pools for each call wastes resources; using a generic exception catch in every consumer repeats provider logic and risks raw diagnostics.

### 6. Usage scopes follow credentials

`llmUsageToday` follows the effective default selection's resolved credential and native model. Shared totals use `IgnoreQueryFilters()` only with an explicit shared source/Gemini/null-personal-FK predicate. Personal totals stay within the current user's key ID. `usageDaily(days, credentialId)` groups by reporting day, source/credential, provider, and native model; its default visibility is shared aggregates plus this user's personal keys, including retired keys. Foreign IDs return a safe unavailable error. Neither query exposes another user's personal activity.

Return provider/native model/KeySource/CredentialId attribution in both DTOs. Keep Pacific day bucketing for compatibility and call it the reporting day for personal keys. Shared configured RPD limits apply only to shared Gemini. Personal limits and remaining requests are null; provider quota/billing information is not inferred from the application's counts. Totals measure this application's attempts, not activity from other apps using the same key.

Move price lookup to provider/model pairs; preserve legacy `Llm:Pricing:{model}` as a Gemini-only fallback. Estimates remain optional approximate figures, not invoices. Historical rows receive Shared attribution without changing tokens or request counts.

Alternatives: KeySource alone would merge successive personal credentials; summing all user rows would leak personal activity and inflate shared usage; attaching configured shared quotas to personal keys would report unverified limits.

## Risks / Trade-offs

- Credential secrets and diagnostic content can escape through SDK exceptions or HTTP logs -> log stable codes/operation IDs only, configure header/query redaction, and use adversarial fake exception/request tests.
- Persisted ciphertext depends on the encryption key ring -> back up both; test protection round trips across reconstructed providers and document safe failure when key material is lost.
- Metadata validation does not prove inference availability -> explain this in API guidance and preserve clear errors with no fallback.
- Provider SDKs differ in JSON options, output caps, token semantics, retries, and lifetime -> verify mapped requests with fake transports and pin compatible versions; keep choices constrained to supported text models.
- Expanded history adds rows for different credentials with the same model -> publish attribution fields and explain the UI must group on credential/provider/model rather than model alone.
- Recording usage cannot establish provider-wide remaining quota -> label configured shared counts as estimates and return no personal quota limit.
- Replacing a secret while a call runs cannot revoke already-sent requests -> retire atomically for future calls and preserve the already-captured credential attribution.

## Migration Plan

1. Use `make migration name=AddUserProviderKeys`, which backs up real database files first. Review `Up()` and any SQLite rebuild for data preservation; do not apply to or restart the running owner API during implementation.
2. Create credential history and usage attribution/FKs/indexes. Backfill existing rows as Shared without modifying tokens/models/timestamps. Preserve existing Gemini settings, IDs, and parse jobs.
3. Deploy persistent key-ring configuration and the reviewed migration together. Ensure the container runtime can access `/data/dp-keys`; preserve that directory on future deployments. Development instructions use an explicit persistent path.
4. Regenerate `schema.graphql`, publish credential/usage/model examples and error handling for the UI, update relevant STATUS sections and rationale notes during implementation.
5. Run migration preservation, credential persistence, ownership, provider mapping, and usage tests with temporary databases/fake providers, then the full suite once.
6. Roll back by restoring a compatible database backup and its matching key ring after explicitly coordinating downtime; reverting only the binaries against a newer schema is not the recovery plan. Keys saved after the backup would require re-entry.

## Sources

- [Microsoft Data Protection key storage](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers): explicit filesystem persistence changes the default key-ring encryption behavior.
- [Microsoft.Extensions.AI.OpenAI package](https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI/): adapter for OpenAI and compatible endpoints.
- [Anthropic C# SDK](https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/csharp): native IChatClient integration, provider exceptions, retry and timeout controls.
- [Mistral migration guide](https://docs.mistral.ai/resources/migration-guides): compatible chat endpoint configuration.
- [Gemini model-list API](https://ai.google.dev/api/models) and [Anthropic model-list API](https://platform.claude.com/docs/en/api/models/list): metadata validation operations.
