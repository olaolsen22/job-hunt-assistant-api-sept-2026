# Spec Delta

## MODIFIED Requirements

### Requirement: Usage today
`llmUsageToday` SHALL return requests and input/output tokens for the current user's effective selection and credential since midnight Pacific, plus provider, native model, credential ID, key source, and next reset. Shared-key totals SHALL cover all shared-key users; personal totals SHALL cover only the current user's selected credential. Failed attempts SHALL count as requests and missing tokens as zero. Other users' personal activity SHALL be excluded.

#### Scenario: Calls from several users
- **WHEN** the Owner makes 2 shared-key calls and the Guest makes 1 shared-key call with the same model today
- **THEN** the Guest sees 3 requests and their summed tokens

#### Scenario: Failed call
- **WHEN** a selected-credential call failed with no token counts today
- **THEN** it contributes one request and zero tokens

#### Scenario: Before the daily reset
- **WHEN** a call predates the latest midnight Pacific
- **THEN** it is excluded

#### Scenario: Other model
- **WHEN** a selected-credential call uses a different native model
- **THEN** it is excluded from today's effective-selection totals

#### Scenario: Owner with a different default
- **WHEN** the Owner's default is model B and the Guest's is model A
- **THEN** each receives totals for their own effective model and resolved credential

#### Scenario: Next reset time
- **WHEN** a user queries `llmUsageToday`
- **THEN** `resetsAt` is next midnight Pacific as a UTC timestamp

#### Scenario: Personal keys use the same model
- **WHEN** two Owners and the Guest use Gemini model A with distinct personal/shared credentials
- **THEN** each Owner's personal total excludes the other Owner and shared calls
- **AND** the Guest's shared total excludes both personal keys

### Requirement: Remaining requests
For a shared Gemini selection with a configured model daily limit, `llmUsageToday` SHALL return that limit and remaining requests floored at zero. Personal selections and shared models without configured limits SHALL return null for both fields. The reporting day SHALL NOT imply a provider's actual billing or quota reset for personal keys.

#### Scenario: Limit configured
- **WHEN** a shared model's limit is 20 and 3 shared requests were made
- **THEN** the limit is 20 and remaining requests is 17

#### Scenario: Over the limit
- **WHEN** a shared model's limit is 20 and 25 shared requests were made
- **THEN** remaining requests is 0

#### Scenario: No limit configured
- **WHEN** a shared model has no configured limit
- **THEN** both fields are null

#### Scenario: Per-model limit
- **WHEN** shared model A has limit 500 and shared model B has limit 250 and the user's default is B
- **THEN** the reported limit is 250

#### Scenario: Personal Gemini key
- **WHEN** the user has a personal Gemini key for a model with a configured shared limit
- **THEN** the personal result has null limit and remaining requests

### Requirement: Daily usage history
`usageDaily(days, credentialId)` SHALL return one row per Pacific day, credential, provider, and native model for the last `days` days (default 30, range 1–90). Without a credential filter it SHALL include shared-key aggregates and only the current user's personal history, including retired keys. Empty groups SHALL be omitted. Rows SHALL include attribution and estimated cost from provider/model prices, or null when missing. Foreign credential filters SHALL be rejected.

#### Scenario: Several days and models
- **WHEN** calls were made on two days and one day has two models on the same credential
- **THEN** three rows are returned with that day's credential/model totals

#### Scenario: Pacific day boundary
- **WHEN** calls occurred at 23:30 and 00:30 Pacific
- **THEN** they appear on separate reporting days

#### Scenario: Estimated cost
- **WHEN** a provider/model costs 0.10 input and 0.40 output per million tokens and a group uses 1000000 input and 500000 output tokens
- **THEN** the estimated cost is 0.30

#### Scenario: No prices configured
- **WHEN** a provider/model has no configured prices
- **THEN** its estimated cost is null

#### Scenario: Days out of range
- **WHEN** `days` is 0 or 91
- **THEN** the query returns an error and no rows

#### Scenario: Retired key history
- **WHEN** the Owner filters daily history by their retired credential ID
- **THEN** only that credential's historical groups are returned
- **AND** the removed secret is not needed to read them

#### Scenario: Foreign key filter
- **WHEN** one Owner filters by another Owner's credential ID
- **THEN** a safe unavailable-credential error is returned without that Owner's totals

## ADDED Requirements

### Requirement: Durable call attribution
Every built-in AI attempt SHALL record the actor, resolved provider/native model, key source, and immutable safe credential ID. Existing usage SHALL retain its counts and be classified as shared Gemini history. Replacing or removing a personal key SHALL NOT reattribute or delete its usage. Model-list key validation SHALL NOT create inference usage rows.

#### Scenario: Upgrade existing history
- **WHEN** the application upgrades a database containing earlier Gemini usage
- **THEN** all previous request and token counts remain visible as shared-key history

#### Scenario: Credential replacement
- **WHEN** an Owner replaces a personal key and makes another call
- **THEN** previous and new calls belong to different credential groups

