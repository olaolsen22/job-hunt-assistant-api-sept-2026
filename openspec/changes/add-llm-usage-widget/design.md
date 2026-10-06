# Design

## Context

See proposal.md for motivation and `specs/llm-usage` for requirements. `LlmUsage` rows exist (one per LLM call, UTC
`CreatedAt`, nullable tokens) with a per-user global query filter. PLAN.md §7 lists `usageToday`, `usageDaily` and a price table as
planned; this change builds `usageToday`, `usageDaily` (all-users) and the widget slot, not the per-user usage stats
or settings. Gemini resets its daily request
limit at midnight Pacific time and has no remaining-quota API (checked against the Gemini rate-limit docs).

## Goals / Non-Goals

**Goals:**
- Let any signed-in user see requests and tokens used today and the requests left, from existing rows.
- Give a daily token and estimated-cost history so the owner can see the trend and predict costs.

**Non-Goals:**
- A daily token budget, Google's per-minute limits, a server-side monthly projection, the guest cap (M7), per-user key,
  limit or price storage.

## Decisions

**Totals cover all users, via `IgnoreQueryFilters()` on `LlmUsages`.**
One key means one quota, so "remaining" is only right if it counts every user's calls. The queries return sums, not rows, so nothing about another user leaks beyond the sum. This is the second place the user filter is bypassed
on purpose (`LookupSeeder` is the first), so the queries carry a comment saying why. Alternative: the caller's own
rows only. That shows a Guest "0 used" while the Owner's quota is gone, which is the opposite of the point.

**The day window is computed with `TimeZoneInfo` for `America/Los_Angeles` and `TimeProvider`.**
Start = midnight of today's Pacific date, converted to UTC; `resetsAt` = start + 1 day in Pacific, converted to UTC.
Convert the local midnight, not "UTC minus 8h", so daylight saving shifts are handled. Compare with `CreatedAt >= start`
in SQL; `CreatedAt` is already UTC. The Pacific zone id is a constant next to the query, not config: Google defines the
reset, not the deployment.

**The request limit is one optional int, `Llm:Gemini:RequestsPerDay`, on `GeminiOptions`.**
Limits change per model and account, and the docs point to AI Studio for them, so the owner copies the number into
config. It is left unset in `appsettings.json` (the true value is unknown), so `requestsLimit`/`remainingRequests` are
null until set. Alternative: a DB table of limits, rejected as unneeded until keys are entered in the UI.

**`llmUsageToday` only counts the configured model (`Llm:Gemini:Model`).**
Google's limits are per model (AI Studio lists a separate limit per model), so another model's calls must not use up
this model's 500. The history query does not filter by model, so a model switch doesn't hide past usage.

**The limit is prefilled to 500 in `appsettings.json`.**
Taken from the AI Studio rate-limit page (free tier, `gemini-3.1-flash-lite`: 500 requests per day, 15 per minute,
250K tokens per minute). It is still just config the owner can edit when the limit or model changes.

**Failed calls count as requests; null tokens count as 0.**
Gemini counts a failed request toward the daily limit, but a call that failed before usage was reported has no tokens.
`SUM` over nullable columns ignores nulls, so the query casts to 0 with `?? 0`.

**`usageDaily` reads the range's rows and groups them in C#.**
Bucketing by Pacific day across a daylight-saving change is awkward to express in SQLite, and at most 90 days of rows
is small. Query `CreatedAt >= start of the first Pacific day`, then group by (Pacific date, model) and sum. Days
without calls produce no row; the UI fills gaps. The UI projects the monthly cost from these rows, so the API adds no
projection field.

**Estimated cost uses a `Llm:Pricing` dictionary keyed by model.**
`{InputPerMTok, OutputPerMTok}` as `decimal`, with cost = tokens / 1,000,000 × price. No entry for a model means
`estimatedCost` is null, not 0, so a missing price can't look like a free model. The keys follow PLAN.md §7. No prices
are prefilled: none of the data we have shows them, and the free tier costs $0, so this is a "would cost" estimate.

**Both queries return plain objects, not `IQueryable`.**
`llmUsageToday` is a single aggregate row and `usageDaily` is at most 90 days of grouped rows, like `dashboardLayout`'s
fixed list: paging, filtering and projection add nothing. Deliberate exception to the "IQueryable" convention.

**The widget is a new last `DashboardWidgetKind` member, `LlmUsage` (`LLM_USAGE` in GraphQL).**
Declaration order is the default order, and `dashboardLayout` already appends widgets without a saved row, shown, so
old layouts need no migration. `saveDashboardLayout` already validates against `Enum.GetValues`, so it requires ten
automatically. The `dashboard-layout` change that introduced the nine-widget rule is not yet archived into
`openspec/specs`, so there is no main spec to MODIFY; the new widget requirement lives in `llm-usage` instead, and
the archive step should fold both together.

## Risks / Trade-offs

- [The configured limit goes stale if Google changes the free tier] → It is one config value, shown as "limit" only;
  document in STATUS.md that it is copied from AI Studio.
- [Own count can drift from Google's, e.g. other apps on the same key or project] → Accepted; the widget is an
  estimate, as PLAN.md already says ("Own count; Gemini has no remaining-quota API").
- [Configured prices go stale] → One config section the owner edits; the estimate is labeled as an estimate.
- [`SUM` over all users' rows scans a growing table] → One day's rows, filtered by `CreatedAt`; add an index only if
  it ever matters.
- [Docker image lacks time zone data, so `FindSystemTimeZoneById` throws at first query] → Task 3.1 checks it in the
  built image before the change is called done.
- [Existing UI sends nine widgets and gets `InvalidDashboardLayoutError` on save] → Announced as breaking; the UI
  change adds `LLM_USAGE` to its registry.

## Migration Plan

No schema change and no migration. Deploy the API, then the UI update. Roll back by reverting. Once the UI has saved a
layout, rows with `LlmUsage` exist and would no longer parse, so delete them first
(`DELETE FROM DashboardWidgets WHERE Widget = 'LlmUsage'`).
