# Spec Delta

## Purpose

Reports how much of the shared LLM request quota has been used today, plus daily usage and estimated cost history, so
the dashboard can show a usage widget. All users share one provider key, so the numbers cover every user's calls.

## ADDED Requirements

### Requirement: Usage today
`llmUsageToday` SHALL return the number of LLM calls and the input and output tokens used by all users with the
configured model since the last midnight Pacific time, plus the time of the next reset. Failed calls SHALL count as requests, and calls without
reported tokens SHALL add 0 tokens. It SHALL be available to every signed-in user and SHALL return totals only, never
another user's individual calls.

#### Scenario: Calls from several users
- **WHEN** the Owner has made 2 calls and a Guest 1 call today, and the Guest queries `llmUsageToday`
- **THEN** `requests` is 3 and the token fields are the sums over all 3 calls

#### Scenario: Failed call
- **WHEN** a call failed with no token counts today
- **THEN** it is included in `requests` and adds 0 to both token totals

#### Scenario: Before the daily reset
- **WHEN** a call was made before the most recent midnight Pacific time
- **THEN** it is not included in the totals

#### Scenario: Other model
- **WHEN** a call today used a model other than the configured one
- **THEN** it is not included in the totals

#### Scenario: Next reset time
- **WHEN** a user queries `llmUsageToday`
- **THEN** `resetsAt` is the next midnight Pacific time, as a UTC timestamp

### Requirement: Remaining requests
When a daily request limit is configured, `llmUsageToday` SHALL return it as `requestsLimit` and the requests left as
`remainingRequests`, never below 0. When no limit is configured, both SHALL be null.

#### Scenario: Limit configured
- **WHEN** the limit is 20 and 3 requests were made today
- **THEN** `requestsLimit` is 20 and `remainingRequests` is 17

#### Scenario: Over the limit
- **WHEN** the limit is 20 and 25 requests were made today
- **THEN** `remainingRequests` is 0

#### Scenario: No limit configured
- **WHEN** no daily limit is configured
- **THEN** `requestsLimit` and `remainingRequests` are null

### Requirement: Daily usage history
`usageDaily(days)` SHALL return one row per Pacific day and model with the requests, input tokens and output tokens of
all users, for the last `days` days ending today (default 30, minimum 1, maximum 90). Days and models without calls
SHALL be omitted. Each row SHALL carry an estimated cost, computed from configured per-model input and output prices per
million tokens, or null when the model has no configured prices. A `days` value outside 1-90 SHALL produce an error. It
SHALL be available to every signed-in user and SHALL return totals only.

#### Scenario: Several days and models
- **WHEN** calls were made on two days, one of them with two models
- **THEN** the result has three rows, each with that day's and model's request and token totals

#### Scenario: Pacific day boundary
- **WHEN** a call was made at 23:30 Pacific time and another at 00:30 Pacific time the next day
- **THEN** they fall in two different days

#### Scenario: Estimated cost
- **WHEN** a model has an input price of 0.10 and an output price of 0.40 per million tokens, and used 1000000 input and
  500000 output tokens on a day
- **THEN** that row's `estimatedCost` is 0.30

#### Scenario: No prices configured
- **WHEN** a model has no configured prices
- **THEN** its rows have a null `estimatedCost`

#### Scenario: Days out of range
- **WHEN** `days` is 0 or 91
- **THEN** the query returns an error and no rows

### Requirement: Usage dashboard widget
The dashboard layout SHALL include a usage widget as its tenth widget, after the existing nine, shown by default.
Users who saved a layout before it existed SHALL get it appended and shown. `saveDashboardLayout` SHALL require all ten
widgets once each.

#### Scenario: Layout saved before the widget existed
- **WHEN** a user with a saved nine-widget layout queries `dashboardLayout`
- **THEN** the result has their nine widgets in their order, followed by the usage widget shown

#### Scenario: Save without the usage widget
- **WHEN** a user saves a layout that omits the usage widget
- **THEN** the mutation returns `InvalidDashboardLayoutError` and the stored layout is unchanged
