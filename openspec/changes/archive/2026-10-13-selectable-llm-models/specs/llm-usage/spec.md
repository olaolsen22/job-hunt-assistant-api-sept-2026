# Spec Delta

## MODIFIED Requirements

### Requirement: Usage today
`llmUsageToday` SHALL return the number of LLM calls and the input and output tokens used by all users with the
current user's effective default model since the last midnight Pacific time, plus that model id and the time of the next reset. Failed calls SHALL count as requests, and calls without
reported tokens SHALL add 0 tokens. It SHALL be available to every signed-in user and SHALL return totals only, never
another user's individual calls.

#### Scenario: Calls from several users
- **WHEN** the Owner has made 2 calls and a Guest 1 call today with the same model, and the Guest queries `llmUsageToday`
- **THEN** `requests` is 3 and the token fields are the sums over all 3 calls

#### Scenario: Failed call
- **WHEN** a call failed with no token counts today
- **THEN** it is included in `requests` and adds 0 to both token totals

#### Scenario: Before the daily reset
- **WHEN** a call was made before the most recent midnight Pacific time
- **THEN** it is not included in the totals

#### Scenario: Other model
- **WHEN** a call today used a model other than the current user's effective default
- **THEN** it is not included in the totals

#### Scenario: Owner with a different default
- **WHEN** the Owner's effective default is model B and the Guest's is model A
- **THEN** the Owner's `llmUsageToday` counts model B calls and returns `model` B, and the Guest's counts model A calls

#### Scenario: Next reset time
- **WHEN** a user queries `llmUsageToday`
- **THEN** `resetsAt` is the next midnight Pacific time, as a UTC timestamp

### Requirement: Remaining requests
When the reported model has a configured daily request limit, `llmUsageToday` SHALL return it as `requestsLimit` and
the requests left as `remainingRequests`, never below 0. When that model has no configured limit, both SHALL be null.

#### Scenario: Limit configured
- **WHEN** the limit is 20 and 3 requests were made today
- **THEN** `requestsLimit` is 20 and `remainingRequests` is 17

#### Scenario: Over the limit
- **WHEN** the limit is 20 and 25 requests were made today
- **THEN** `remainingRequests` is 0

#### Scenario: No limit configured
- **WHEN** the reported model has no daily limit configured
- **THEN** `requestsLimit` and `remainingRequests` are null

#### Scenario: Per-model limit
- **WHEN** model A has a limit of 500 and model B a limit of 250, and the Owner's effective default is B
- **THEN** the Owner's `requestsLimit` is 250
