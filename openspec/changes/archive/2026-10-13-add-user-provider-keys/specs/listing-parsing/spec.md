# Spec Delta

## MODIFIED Requirements

### Requirement: Provider failures fail the job
A provider failure SHALL mark the ParseJob FAILED with a safe readable error instead of failing the request: authentication/authorization failure, rate limit, unreachable or erroring provider, and timeout. Messages SHALL work for personal paid keys and the shared free key. Personal-key failures SHALL never trigger another credential or provider.

#### Scenario: Rate limited
- **WHEN** the provider rejects the call with HTTP 429
- **THEN** the ParseJob is FAILED with an error describing a provider rate limit
- **AND** no alternative-key call is attempted

#### Scenario: Timeout
- **WHEN** the provider call times out
- **THEN** the ParseJob is FAILED with an error saying the AI took too long and the ParseJob is saved

#### Scenario: Personal key revoked
- **WHEN** the provider rejects a configured personal key
- **THEN** the ParseJob is FAILED with a safe credential error
- **AND** neither the raw provider response nor key is returned

### Requirement: Every provider call is logged
Each attempted provider call SHALL write one usage row with actor, provider, native model, key source, credential ID, operation, latency, success, available tokens, and a safe failure code. Codes SHALL distinguish `invalid-json`, `invalid-key`, `rate-limited`, `provider-error`, and `timeout`. SDK-internal retries SHALL NOT conceal extra inference attempts from this accounting.

#### Scenario: Usage after success
- **WHEN** a parse succeeds with 1200 input and 300 output tokens
- **THEN** a successful row records those counts and the actual credential attribution

#### Scenario: Usage after timeout
- **WHEN** the provider attempt times out
- **THEN** a failed row with `timeout` records the attempted credential

