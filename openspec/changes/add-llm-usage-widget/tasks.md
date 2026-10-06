# Tasks

## 1. Widget slot

- [x] 1.1 Add `LlmUsage` as the last member of `DashboardWidgetKind` (`Features/Dashboard/DashboardWidget.cs`) and add `LLM_USAGE` to the `Default` array in `DashboardTests`; verify `DashboardTests` passes, including the save-reorder tests, which now send ten widgets
- [x] 1.2 Add a `DashboardTests` case for a user whose saved layout has the nine old widgets: `dashboardLayout` returns their order, then `LLM_USAGE` shown; and a save omitting `LLM_USAGE` → `InvalidDashboardLayoutError` with the stored layout unchanged; verify both pass

## 2. Usage queries

- [x] 2.1 Add optional `int? RequestsPerDay` to `GeminiOptions` (comment: Gemini has no API for it, copied from AI Studio) and set `"RequestsPerDay": 500` under `Llm:Gemini` in `appsettings.json` (free-tier limit for `gemini-3.1-flash-lite`, from the AI Studio rate-limit page on 2026-10-06); add a pricing options class bound from `Llm:Pricing` (model → `InputPerMTok`, `OutputPerMTok` as `decimal`), with no prices set in `appsettings.json`; verify `dotnet build` passes and the app still starts with no prices
- [x] 2.2 Add `Features/Usage/UsageQueries.cs` with `[QueryType]` `GetLlmUsageToday`: `IgnoreQueryFilters()` on `LlmUsages` (comment why: one shared key, aggregates only), only rows where `Model` is `Llm:Gemini:Model`, Pacific day start/`resetsAt` from `TimeProvider` and `America/Los_Angeles`, `requests`/`inputTokens`/`outputTokens` (null tokens as 0), `requestsLimit`/`remainingRequests` (limit − requests, floored at 0, null without a limit); verify build passes
- [x] 2.3 Add `UsageTests` for `llmUsageToday` (rows dated relative to the real clock, since the test project has no fake clock): calls from Owner and Guest summed for a Guest caller, failed call counted with 0 tokens, call before midnight Pacific excluded, call with another model excluded, `resetsAt` is the next midnight Pacific, limit 500 (from `appsettings.json`) with 3 used → 497, over the limit → 0, no limit → null/null; also unit-test `PacificDay` on a winter date, a summer date and the daylight-saving start (23-hour day); verify all pass
- [x] 2.4 Add `UsageQueries.GetUsageDaily(days = 30)`: reject `days` outside 1–90 with a top-level error, read rows from the start of the first Pacific day (all users, all models, `IgnoreQueryFilters()` with the same comment), group in C# by (Pacific date, model) and sum, `estimatedCost` = input tokens / 1,000,000 × `InputPerMTok` + output tokens / 1,000,000 × `OutputPerMTok`, null when the model has no price entry; verify build passes
- [x] 2.5 Add `UsageTests` for `usageDaily`: several days and models give one row each, calls at 23:30 and 00:30 Pacific land on different days, cost 0.30 for 1000000 in + 500000 out at 0.10 / 0.40, null cost without prices, `days` 0 and 91 → error, another user's calls are included as totals; verify all pass

## 3. Docs and final check

- [ ] 3.1 Check that `/usr/share/zoneinfo/America/Los_Angeles` exists in the `mcr.microsoft.com/dotnet/aspnet:10.0` image (`docker run --rm --entrypoint ls <image> /usr/share/zoneinfo/America/Los_Angeles`); if it is missing, install `tzdata` in the final stage of `Dockerfile`; verify the file is present
- [x] 3.2 Regenerate `schema.graphql`, update `docs/STATUS.md` (`llmUsageToday`, `usageDaily`, `LLM_USAGE` widget, `Llm:Gemini:RequestsPerDay` and `Llm:Pricing` keys, ten widgets, breaking save) with `/log-progress`, append the why to `.claude/commit-notes.md`; verify `SchemaTests` passes and STATUS lists both queries and the config keys
- [x] 3.3 Run `dotnet test` and `openspec validate add-llm-usage-widget`; verify both pass and `schema.graphql` is current
