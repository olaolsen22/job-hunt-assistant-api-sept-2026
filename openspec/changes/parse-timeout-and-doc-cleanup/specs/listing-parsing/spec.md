# Spec Delta

## Purpose

Turns a pasted job ad into listing JSON with an LLM, reports every outcome on a ParseJob the user can read, and logs
every provider call for usage tracking.

## ADDED Requirements

### Requirement: Parse outcome
`parseListing` SHALL return a ParseJob that is SUCCEEDED with `result` JSON (an object for FULL, an array for QUICK)
or FAILED with a readable `error`. Blank text or text over 100,000 characters MUST fail with
`InvalidParseRequestError` and create no ParseJob.

#### Scenario: Successful full parse
- **WHEN** the provider answers with a JSON object for a FULL parse
- **THEN** the ParseJob is SUCCEEDED and `result` is that JSON

#### Scenario: Wrong JSON shape
- **WHEN** the provider answers with an object for a QUICK parse
- **THEN** the ParseJob is FAILED and `result` is null

### Requirement: Provider failures fail the job
A provider failure SHALL mark the ParseJob FAILED with a readable error instead of failing the request: rate limit
("free-tier limit"), unreachable or erroring provider ("couldn't be reached"), and timeout ("took too long").

#### Scenario: Rate limited
- **WHEN** the provider rejects the call with HTTP 429
- **THEN** the ParseJob is FAILED with an error mentioning the free-tier limit

#### Scenario: Timeout
- **WHEN** the provider call times out
- **THEN** the ParseJob is FAILED with an error saying the AI took too long, and the ParseJob is saved

### Requirement: Every provider call is logged
Each provider call SHALL write one usage row with provider, model, operation, latency and success, plus tokens when the
provider reports them and an error code on failure: `invalid-json`, `rate-limited`, `provider-error` or `timeout`.

#### Scenario: Usage after success
- **WHEN** a parse succeeds and the provider reports 1200 input and 300 output tokens
- **THEN** a successful usage row with those token counts is saved

#### Scenario: Usage after timeout
- **WHEN** the provider call times out
- **THEN** a failed usage row with error code `timeout` is saved

### Requirement: Parse jobs are private
A ParseJob SHALL be readable only by the user who created it; `parseJob(id)` for another user's job MUST return null.

#### Scenario: Other user's job
- **WHEN** the guest queries the owner's ParseJob id
- **THEN** the result is null
