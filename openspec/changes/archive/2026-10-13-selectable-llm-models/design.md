# Design

## Context

- `Program.cs` registers one keyed `IChatClient` (`"gemini"`), built with the single `GeminiOptions.Model`.
  `ListingParser`, `ProfileCvParser` and `TailoredCvAnalyzer` all inject it, and each writes `gemini.Value.Model` to
  `LlmUsage.Model`.
- Parse jobs run inline in the mutation (no background queue yet). `ParseJob` stores `Provider` but not the model.
- `llmUsageToday` filters on `GeminiOptions.Model` and reads `GeminiOptions.RequestsPerDay`.
- Roles: `Owner` and `Guest` (seeded). Per-user rows use `UserId` plus a global query filter on
  `AppDbContext.CurrentUserId`.
- Plan items that **don't exist in code yet**: an LLM token-bucket limiter and a Guest daily AI cap. This change
  neither adds nor depends on them.

## Goals / Non-Goals

**Goals:**
- The server is the only gate for which model a user may use, so the UI can be the same for both accounts.
- Keep one client per provider; the model is chosen per call.

**Non-Goals:**
- Per-user or per-session API keys, a second provider, a per-model rate limiter, or a Guest cap.
- Changing how the shared parse cache works (none exists yet).

## Decisions

### 1. Model per call through `ChatOptions.ModelId`
In Microsoft.Extensions.AI, `ChatOptions.ModelId` overrides the client's default model for that call, so the keyed
singleton stays. The client is built with the configured default model, which is used only if a caller forgets to set
`ModelId`.
*Alternative:* one keyed client per model. Rejected: it multiplies registrations without gaining anything.

### 2. Allowlist in config
```json
"Llm": { "Gemini": {
  "DefaultModel": "gemini-3.1-flash-lite",
  "Models": [
    { "Id": "gemini-3.1-flash-lite", "Label": "Flash-Lite", "RequestsPerDay": 500 },
    { "Id": "gemini-3.1-flash",      "Label": "Flash",      "RequestsPerDay": 250 }
  ] } }
```
`GeminiOptions` gains `DefaultModel` and `Models` and loses `Model` and `RequestsPerDay`. Startup validation
(`Validate(...)` + `ValidateOnStart`) requires at least one model, unique ids, and `DefaultModel` in `Models`, so a
bad config fails at boot instead of at the first AI call. Config lives in `appsettings.json`; only the key is secret.
*Alternative:* store the list in the DB. Rejected: it's operator config, not user data.

### 3. `LlmModelResolver` (scoped, `Llm/`)
- `AllowedModels()`: all configured models for Owner; `[DefaultModel]` for Guest (`ClaimsPrincipal.IsInRole`).
- `ResolveAsync(string? requested, ct)`: requested → stored default → config default. A `requested` value that is not
  in `AllowedModels()` (ordinal comparison) throws `UnknownLlmModelException`. A stale stored default falls through
  silently.
- Built-in role checks are enough here because there are only two roles. Any other signed-in user without the Owner role is treated
  like Guest (least privilege).
Parsers take the resolved model id as a parameter instead of reading `GeminiOptions`, so the mutation resolves it once
before any AI work starts.

### 4. `UserLlmSetting` entity (`Llm/` or `Features/LlmModels/`)
`Id`, `UserId` (FK cascade, unique index), `Provider` (`"gemini"`), `DefaultModel` (nullable, max 100). There's a global
filter on `UserId`. `setDefaultLlmModel` upserts the current user's row. It has no `userId` argument, so the filter
and `CurrentUserId` are the only way to reach the row. Storing `Provider` now lets a second provider be added later
without a table rebuild.
*Alternative:* a column on `AppUser`. Rejected: it mixes Identity with feature data, and the vertical-slice rule puts the
entity in its own slice.

### 5. GraphQL surface
- `llmModels: LlmModels { models: [LlmModel { id, label }], defaultModel }` lives in a new `Features/LlmModels` slice.
  `RequestsPerDay` is left out: the usage widget already reports the limit.
- `setDefaultLlmModel(model: String): LlmModels` with `[Error<UnknownLlmModelException>]`.
- The AI mutations get `string? model` and `[Error<UnknownLlmModelException>]`. Input is capped at 100 characters
  (DataAnnotations on the argument, or an explicit check that throws the same typed error). The error message is fixed
  ("That model isn't available.") and doesn't echo the input.

### 6. Model on ParseJob
A nullable `Model` column is added. New jobs always set it, and the column stays nullable so existing rows survive the
migration without a backfill. When the queue arrives, the worker uses `job.Model` as it is, so a job runs with the
model that was chosen when it was submitted.

### 7. Usage today
`llmUsageToday` resolves the caller's effective default and counts that model across all users, as before. It
takes the limit from that model's `RequestsPerDay` and adds a `model` field so the widget can label it.

## Security

| Threat | Mitigation |
|---|---|
| Cost abuse: client sends an expensive or arbitrary model id | Exact match against the per-user allowlist before any SDK call; unknown → typed error, no call, no usage row |
| Guest escalates by calling the API directly | Allowlist is computed server-side from the role; UI filtering is cosmetic |
| Cross-user tampering with settings | No `userId` argument; global query filter + `CurrentUserId` on write; test with two users |
| Oversized or odd input stored | Max length 100; only allowlisted ids are ever stored |
| Key leakage | `llmModels` exposes id and label only; errors use fixed messages; keys stay in user-secrets or env |
| Stale config breaks a user | A removed stored default falls back to the config default; startup validation catches a bad allowlist |

## Risks / Trade-offs

- [Breaking config shape] An env override like `Llm__Gemini__Model` would be ignored silently → `docker-compose.yml`
  only sets the key today. Startup validation fails if `Models` or `DefaultModel` is missing.
- [Models have different free-tier limits] Users can switch to a model with a lower limit → each model's limit is
  shown in the widget. Rate limiting stays out of scope.
- [The Guest role check is the only gate] → covered by Guest tests for each AI mutation that use a configured,
  non-default model.

## Migration Plan

1. `make backup`.
2. `make migration name=AddLlmModelSelection`: new `UserLlmSettings` table plus a nullable `ParseJobs.Model`. Check that
   `Up()` contains only `CreateTable`, `AddColumn` and `CreateIndex` (no table rebuild of `ParseJobs`).
3. Update `appsettings.json` to the new `Llm:Gemini` shape.
Rollback: restore the backup and revert the commit. Existing `LlmUsage` rows already carry `Model`.
