# Proposal

## Why

Every AI call uses the single model configured in `Llm:Gemini:Model`. To try a stronger model you have to edit
config and restart the API. The Owner should be able to choose a default model and override it for each AI action. The
Guest must stay on the cheap default, and the UI should be the same for both accounts.

## What Changes

- Config lists the allowed Gemini models (id, label, optional daily request limit) and a default model. These replace
  the single `Model` and `RequestsPerDay` settings (**BREAKING** for config only: `Llm:Gemini:Model` and
  `Llm:Gemini:RequestsPerDay` are removed).
- New `llmModels` query: the current user's allowed models and their effective default. The Owner gets every configured
  model; the Guest gets only the configured default.
- New `setDefaultLlmModel` mutation: stores or clears the current user's default model.
- `parseListing`, `parseListingFromUrl`, `parseUrl`, `parseProfileCv`, `analyzeTailoredCvSection` and
  `analyzeAllTailoredCv` accept an optional `model`. A model outside the user's allowed list is rejected with a
  typed `UnknownLlmModelError`. No AI call is made and nothing is logged.
- Each call uses the model resolved in this order: input, then user default, then config default. That model is recorded on the
  usage row and on the parse job.
- `llmUsageToday` reports the current user's effective default model and that model's daily limit.
- API keys stay in user-secrets or environment variables, one per provider. Per-user keys are out of scope.

## Capabilities

### New Capabilities
- `llm-model-selection`: the allowed model list per user, the stored default model, the per-call model override, and
  its validation.

### Modified Capabilities
- `llm-usage`: "Usage today" and "Remaining requests" now follow the user's effective default model and that model's
  configured limit, not the single configured model.

## Impact

- Config: `Llm:Gemini` section shape (`appsettings.json`, any `Llm__Gemini__Model` env override).
- Data: new `UserLlmSettings` table; new `Model` column on `ParseJobs`. Migration only adds a table and a column, but
  `make backup` runs first.
- Code: `Llm/` (options, resolver), `Features/Parsing`, `Features/Profiles/ProfileCvParser`,
  `Features/TailoredCvDrafts/TailoredCvAnalyzer` and its mutations, `Features/Usage/UsageQueries`, `Program.cs`.
- GraphQL schema: new query, mutation, error type, and optional `model` arguments (additive for the UI).
- UI (separate repo): a model picker filled from `llmModels`; no Guest-specific code.
