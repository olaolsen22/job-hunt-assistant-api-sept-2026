# Tasks

## 1. Config and resolver

- [x] 1.1 Reshape `GeminiOptions` (`DefaultModel`, `Models[] { Id, Label, RequestsPerDay? }`; drop `Model`, `RequestsPerDay`) with startup validation (non-empty, unique ids, default in list); update `appsettings.json`, `Program.cs` client registration and the `ApiFactory` test config. Verify that `dotnet test` still starts the host and that a test with an invalid config fails validation.
- [x] 1.2 Add `UserLlmSetting` entity + configuration (unique `UserId`, FK cascade, `Provider`, nullable `DefaultModel` max 100) and global query filter; add nullable `ParseJob.Model`. Run `make backup` then `make migration name=AddLlmModelSelection`; verify `Up()` only creates the table, adds the column and the index.
- [x] 1.3 Add scoped `LlmModelResolver` (allowed list by role, requested → stored → config default, ordinal match, `UnknownLlmModelException` with fixed message); register in `Program.cs`. Verify with tests that Owner gets all models, Guest gets only the default, and a stale stored default falls back.

## 2. Model selection API

- [x] 2.1 Add `Features/LlmModels` slice with `llmModels` query and `setDefaultLlmModel` mutation (`[Error<UnknownLlmModelException>]`, null clears). Verify with tests the spec scenarios for listing, setting, clearing, Guest rejection, unauthenticated rejection, and that Owner's setting doesn't change Guest's.

## 3. Model on AI calls

- [x] 3.1 Add optional `model` (max 100) to `parseListing`, `parseListingFromUrl`, `parseUrl`; resolve before fetching or calling the model; set `ChatOptions.ModelId`, `ParseJob.Model` and `LlmUsage.Model` from the resolved id. Verify with `ParseTests` that the fake chat client receives the override, the job and usage record it, and that Guest/unknown/different-case models return `UnknownLlmModelError` with no job or usage row.
- [x] 3.2 Same for `parseProfileCv` (`ProfileCvParser`). Verify with tests for override, default, and Guest rejection with no usage row.
- [x] 3.3 Same for `analyzeTailoredCvSection` and `analyzeAllTailoredCv` (`TailoredCvAnalyzer`, every section call in analyze-all uses the one resolved model). Verify with `TailoredCvAnalysisTests` for override, default, and rejection.

## 4. Usage

- [x] 4.1 Make `llmUsageToday` use the caller's effective default and that model's `RequestsPerDay`, and add a `model` field. Verify with `UsageTests` for the per-model limit and an Owner and Guest with different defaults; existing scenarios still pass.

## 5. Wrap-up

- [x] 5.1 Regenerate `schema.graphql`, run the full `dotnet test`, and update `docs/STATUS.md` via `log-progress` (model selection, config shape). Verify that the suite is green and the schema diff contains only additive GraphQL changes.
