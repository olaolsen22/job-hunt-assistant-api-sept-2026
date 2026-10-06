# Proposal

## Why

Every LLM call already writes an `LlmUsage` row, but nothing exposes it, so the UI can't show how close the Gemini free
tier is to its daily limit, or how token use and cost are trending. Gemini has no remaining-quota API, so the API has
to compute it from its own rows. All users share one Gemini key and therefore one quota, so the numbers have to cover
every user's calls, not just the caller's.

## What Changes

- Add query `llmUsageToday` → `{requests, inputTokens, outputTokens, requestsLimit?, remainingRequests?, resetsAt}`.
  - Counts all users' calls to the configured model (`Llm:Gemini:Model`) since the last midnight Pacific time, when
    Gemini resets its daily request limit. Limits are per model, so other models' calls don't count.
  - Failed calls count as requests (Gemini counts them); their null tokens count as 0.
  - `requestsLimit` comes from a new optional config key `Llm:Gemini:RequestsPerDay`, set to 500 (the free-tier limit
    for `gemini-3.1-flash-lite` shown in AI Studio on 2026-10-06). Unset → `requestsLimit` and `remainingRequests` are
    null. `remainingRequests` never goes below 0.
  - `resetsAt` is the next midnight Pacific as a UTC `DateTime`.
- Add query `usageDaily(days: Int = 30)` → one row per Pacific day and model:
  `{day, model, requests, inputTokens, outputTokens, estimatedCost?}`.
  - Covers all users and all models, so history survives a model switch. Days without calls are omitted.
  - `days` must be 1–90, otherwise a top-level error. The range ends today.
  - `estimatedCost` is what the tokens would cost at paid-tier prices from config
    (`Llm:Pricing:{model}:InputPerMTok` / `OutputPerMTok`, as planned in PLAN.md §7). A model without prices → null.
    No prices are set yet, so the owner fills them in.
  - The UI draws the trend and projects a monthly figure from the daily rows; the API sends no projection.
- Add `LLM_USAGE` as a tenth `DashboardWidgetKind`, last in the default order, so the usage widget can be placed
  and hidden like the others. `saveDashboardLayout` now requires all ten widgets. Layouts saved earlier get the new
  widget appended and shown, with no data migration.
- Open to every signed-in user, Owner and Guest. Both queries return aggregates only, never another user's rows.
- No migration, no new package.
- Out of scope: Google's per-minute limits, a daily token budget (Google has no daily token limit), the per-user guest
  cap (M7), storing keys, limits or prices in the database.

## Capabilities

### New Capabilities
- `llm-usage`: reporting today's use of the shared LLM request quota, daily usage and estimated cost history, and the
  dashboard widget that shows them.

### Modified Capabilities

## Impact

- New `Features/Usage` (`UsageQueries`), `GeminiOptions` (`RequestsPerDay`), a pricing options class,
  `appsettings.json`, `DashboardWidgetKind`, `schema.graphql`, `docs/STATUS.md`, and tests. `DashboardTests` asserts the
  default widget list, which gains a tenth entry.
- **BREAKING** for the UI: `saveDashboardLayout` rejects a list without `LLM_USAGE`.
- The Docker image needs time zone data for `America/Los_Angeles`.
