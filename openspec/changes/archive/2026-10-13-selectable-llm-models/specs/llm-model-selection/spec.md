# Spec Delta

## Purpose

Lets each user choose which configured LLM model runs their AI actions, both as a stored default and for each call,
while the server alone decides which models each user may use.

## ADDED Requirements

### Requirement: Allowed models per user
`llmModels` SHALL return the models the current user may use, each with its id and label, plus the user's effective
default model. The Owner SHALL get every configured model. The Guest SHALL get only the configured default model.
The response SHALL NOT contain API keys or other provider credentials.

#### Scenario: Owner lists models
- **WHEN** three models are configured and the Owner queries `llmModels`
- **THEN** all three models are returned

#### Scenario: Guest lists models
- **WHEN** three models are configured and the Guest queries `llmModels`
- **THEN** only the configured default model is returned, and it is the effective default

#### Scenario: Not signed in
- **WHEN** an anonymous request queries `llmModels`
- **THEN** the request is rejected as unauthenticated

### Requirement: Effective default model
A user's effective default SHALL be their stored default when it is in their allowed list. Otherwise it SHALL be the
configured default model.

#### Scenario: No stored default
- **WHEN** the Owner has never set a default
- **THEN** the effective default is the configured default model

#### Scenario: Stored default removed from config
- **WHEN** the Owner's stored default is no longer a configured model
- **THEN** the effective default is the configured default model and no error is returned

### Requirement: Set default model
`setDefaultLlmModel(model)` SHALL store `model` as the current user's default when it is in their allowed list, and
SHALL clear the stored default when `model` is null. It SHALL return the updated `llmModels` result. It SHALL
only ever change the current user's setting.

#### Scenario: Owner sets a default
- **WHEN** the Owner sets an allowed model as default
- **THEN** `llmModels` returns it as the effective default for the Owner, and the Guest's default is unchanged

#### Scenario: Clear the default
- **WHEN** the Owner calls `setDefaultLlmModel` with null
- **THEN** the effective default is the configured default model

#### Scenario: Model not allowed
- **WHEN** the Guest sets a model that is configured but not in the Guest's allowed list
- **THEN** the mutation returns `UnknownLlmModelError` and the stored setting is unchanged

### Requirement: Model per AI call
`parseListing`, `parseListingFromUrl`, `parseUrl`, `parseProfileCv`, `analyzeTailoredCvSection` and
`analyzeAllTailoredCv` SHALL accept an optional `model`. Each call SHALL use `model` when given, otherwise the user's
effective default. The model used SHALL be recorded on the call's usage row, and on the parse job for parse mutations.

#### Scenario: Override for one call
- **WHEN** the Owner's default is model A and the Owner parses a listing with `model` B
- **THEN** the AI call uses B and the parse job and usage row record B

#### Scenario: No override
- **WHEN** the Owner parses a CV without `model` and their default is A
- **THEN** the AI call uses A and the usage row records A

### Requirement: Unknown model rejected
An AI mutation whose `model` is not in the current user's allowed list, compared exactly, SHALL return
`UnknownLlmModelError`. It SHALL make no AI call and store no parse job or usage row. The error message SHALL NOT echo the submitted value.

#### Scenario: Guest requests another model
- **WHEN** the Guest calls `parseListing` with a configured model other than the default
- **THEN** the mutation returns `UnknownLlmModelError` and no AI call or usage row is made

#### Scenario: Arbitrary model id
- **WHEN** the Owner calls `analyzeTailoredCvSection` with a model id that is not configured
- **THEN** the mutation returns `UnknownLlmModelError` and no AI call is made

#### Scenario: Different case
- **WHEN** the Owner sends a configured model id in different letter case
- **THEN** the mutation returns `UnknownLlmModelError`
