# Spec Delta

## Purpose

Lets users control how many personal-key AI attempts this application admits each day, with private settings and race-safe enforcement independent of provider billing.

## ADDED Requirements

### Requirement: User-controlled personal-provider limits
Owners SHALL be able to set and disable their own dailyRequestLimit per supported personal-key provider. Null SHALL disable enforcement; zero SHALL block new personal attempts; positive integers SHALL cap daily attempts. Limits SHALL start disabled. Changing a limit SHALL not reset consumption. Guests and other users SHALL not manage an Owner's settings. Limits SHALL remain configured across key replacement or removal.

#### Scenario: Set and disable a limit
- **WHEN** an Owner sets a provider limit to 10 and later clears it
- **THEN** the provider changes from a 10-attempt daily cap to no application daily cap

#### Scenario: Pause personal inference
- **WHEN** an Owner sets a personal provider limit to zero
- **THEN** new personal inference on that provider is rejected without a provider call

#### Scenario: Foreign settings
- **WHEN** another user queries or changes their own limits
- **THEN** none of the first Owner's settings or consumption is disclosed or altered

### Requirement: Limit scope and reset
A personal-provider limit SHALL aggregate the user's admitted attempts across all that provider's models and current or retired credentials, including failures and canceled or interrupted attempts. The day SHALL reset at midnight Pacific to match existing application usage reporting. Shared-key inference SHALL be exempt, including default free Gemini. Existing shared-provider quota displays SHALL remain estimates, separate from enforced user limits.

#### Scenario: Rotate key or switch models
- **WHEN** a user replaces a personal key or switches models on the same provider
- **THEN** the consumed count remains part of that user's provider-day limit

#### Scenario: Default free Gemini
- **WHEN** a user selects default Gemini using the shared key
- **THEN** personal-provider daily limits are not applied
- **AND** duplicate/concurrency guards still apply

#### Scenario: Personal Gemini
- **WHEN** default Gemini resolves to the user's personal key
- **THEN** the user's configured personal Gemini limit applies

#### Scenario: Daily boundary
- **WHEN** a new personal attempt is admitted after midnight Pacific
- **THEN** it consumes the new day's allowance using the correct daylight-saving offset

### Requirement: Atomic attempt admission
The API SHALL atomically reserve an allowance before every personal inference attempt, including each analyze-all section. When no allowance remains it SHALL return daily-limit-reached without inference, usage inflation, credential fallback, or queued work. Duplicate replays, local input errors, key model-list validation, and credentials rejected before inference SHALL not consume allowance. An admitted attempt with an uncertain delivery outcome SHALL remain counted conservatively.

#### Scenario: Last available allowance
- **WHEN** concurrent admissions compete for one remaining provider-day attempt
- **THEN** at most one reaches inference and the consumed count cannot exceed the configured cap

#### Scenario: Exhausted batch
- **WHEN** analyze-all consumes its last allowance before all sections finish
- **THEN** no further inference calls are made and remaining sections are reported as skipped

#### Scenario: Pre-inference credential failure
- **WHEN** a saved key cannot be decrypted
- **THEN** no attempt allowance is consumed and no other key is called

### Requirement: Private limit reporting
The API SHALL report the current user's provider, configured daily limit, consumed attempts, remaining allowance floored at zero, and next Pacific reset. An unset limit SHALL report null remaining allowance. Consumption SHALL include earlier attempts in the current day when enabling a limit and SHALL distinguish application limits from provider quotas or monetary budgets. Queries SHALL not call a provider or expose credential secrets.

#### Scenario: Enable after using AI
- **WHEN** an Owner has already admitted 7 personal-provider attempts today and sets a limit of 5
- **THEN** reporting returns 7 consumed and 0 remaining and further attempts are blocked

#### Scenario: Limit not configured
- **WHEN** an Owner has no limit for a provider
- **THEN** its configured limit and remaining allowance are null
