# Spec Delta

## Purpose

Prevents accidental repeated AI inference while preserving user isolation, existing review flows, and transient CV data across cancellation and application restarts.

## ADDED Requirements

### Requirement: AI operation identity
All six built-in AI mutations SHALL accept an optional nonempty UUID requestId. A supplied ID SHALL identify one operation within the signed-in user's scope. Reuse for a different operation or different submitted arguments SHALL return a safe request-conflict error without fetching a URL or calling AI. Omission SHALL preserve legacy inputs while enabling active-operation guards.

#### Scenario: Changed request with reused ID
- **WHEN** a user reuses a listing parse requestId with different text or mode
- **THEN** the API returns request-conflict without a fetch, inference attempt, or new usage row

#### Scenario: Independent user identities
- **WHEN** two users submit the same requestId
- **THEN** each is treated independently and neither receives the other's operation data

#### Scenario: Legacy caller
- **WHEN** an existing caller omits requestId
- **THEN** its existing input shape remains accepted subject to the concurrency and limit guards

### Requirement: Safe duplicate responses
A repeated requestId SHALL never silently launch another inference operation. Active repeats SHALL return operation-in-progress. Completed listing operations SHALL return the original owned parse job when available. Completed CV or tailored-analysis repeats SHALL return result-unavailable without persisting or regenerating the transient result. Failed or interrupted operations SHALL require a new requestId for an explicit new attempt.

#### Scenario: Lost listing response
- **WHEN** a completed listing parse response is lost and the user resends its requestId
- **THEN** the API returns the same parse job with no new fetch, inference, or usage

#### Scenario: Lost CV preview
- **WHEN** a user repeats a completed CV parse requestId
- **THEN** the API returns result-unavailable and makes no inference call
- **AND** a new explicit attempt requires a new requestId

#### Scenario: Credential replaced after completion
- **WHEN** a completed operation is repeated after its credential or default model changes
- **THEN** the original receipt is honored without inference on the new selection

### Requirement: One active operation per user
The API SHALL admit at most one top-level AI operation per user at a time. An overlapping identical request SHALL return operation-in-progress; other overlapping AI work SHALL return ai-busy. This SHALL apply across mutations, GraphQL aliases, tabs, requestIds, and shared or personal credentials. Analyze-all section calls SHALL belong to its single admitted operation. Rejected work SHALL not be queued or consume inference usage.

#### Scenario: Concurrent double click
- **WHEN** two matching requests arrive concurrently with different IDs or no IDs
- **THEN** only one operation can reach inference and the other reports operation-in-progress

#### Scenario: Overlapping mutations
- **WHEN** a user starts a CV parse while listing analysis is active
- **THEN** the API returns ai-busy and makes no CV inference call

#### Scenario: Free model safeguards
- **WHEN** shared default Gemini is selected
- **THEN** the same duplicate and concurrency guards apply even though no daily user cap is enforced

### Requirement: Durable attempt safety
Before outbound inference the API SHALL durably record admission of the attempt. Restart, cancellation, or failure to save a result SHALL not automatically resend a possibly submitted attempt. Repeating an interrupted requestId SHALL report operation-interrupted. Recovery SHALL release stale active-operation admission without erasing the original identity or silently resuming it. No claim of provider-side exactly-once execution SHALL be made.

#### Scenario: Restart after request submission
- **WHEN** the process stops after sending inference and is recreated
- **THEN** the original requestId cannot trigger another inference attempt
- **AND** a fresh explicit operation can be admitted after interrupted-operation recovery

#### Scenario: Cancellation after submission
- **WHEN** the caller cancels after an attempt has been admitted
- **THEN** cancellation propagates and its durable attempt remains counted conservatively
- **AND** the original requestId does not restart the operation

### Requirement: Receipts preserve privacy
Operation receipts SHALL retain only safe identity, opaque fingerprints, timestamps, state, safe errors, and existing listing-job references. They SHALL NOT persist raw CV text, CV candidates, tailored-analysis suggestions, prompts, API keys, or provider response bodies. Receipt diagnostics and responses SHALL expose no other user's data. Request identities SHALL survive routine restarts and SHALL not expire silently into repeatable work.

#### Scenario: CV operation receipt
- **WHEN** a CV parse succeeds or fails
- **THEN** its stored receipt contains no CV input or extracted candidate content

#### Scenario: Unknown operation
- **WHEN** a user submits an ID matching another user's historical operation
- **THEN** no historical metadata or result from that user is returned

### Requirement: Safe admission errors
Admission failures SHALL expose stable typed GraphQL error codes for invalid-request-id, request-conflict, operation-in-progress, ai-busy, result-unavailable, operation-interrupted, and daily-limit-reached. Locally rejected work SHALL cause no inference usage row. Existing input/model validation, credential routing, disabled retries, and disabled failover SHALL remain effective.

#### Scenario: Invalid model before inference
- **WHEN** an AI operation names an unavailable model
- **THEN** the existing unknown-model error is returned without fetch or inference

#### Scenario: Busy rejection
- **WHEN** concurrency admission rejects an operation
- **THEN** ai-busy is returned without provider exceptions, request content, or an inference usage row
