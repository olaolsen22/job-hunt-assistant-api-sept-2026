# llm-model-selection Specification

## Purpose

Lets each user choose which configured LLM model runs their AI actions, both as a stored default and for each call,
while the server alone decides which models each user may use.

## Requirements

### Requirement: Allowed models per user
`llmModels` SHALL return allowed models with unique selection IDs, labels, provider, native model ID, key source, and safe credential ID, plus the effective default selection ID. Owners SHALL get configured Gemini models and configured models for providers with their active personal keys. Guests and other non-Owners SHALL get only the shared Gemini default. Existing Gemini selection IDs SHALL remain valid. Credentials SHALL NOT be exposed.

#### Scenario: Owner lists models
- **WHEN** three Gemini models are configured and the Owner queries `llmModels`
- **THEN** all three are returned with Gemini provider and resolved credential metadata

#### Scenario: Guest lists models
- **WHEN** three Gemini models are configured and the Guest queries `llmModels`
- **THEN** only the shared configured default model is returned and it is the effective default

#### Scenario: Not signed in
- **WHEN** an anonymous request queries `llmModels`
- **THEN** the request is rejected as unauthenticated

#### Scenario: Provider key enables configured models
- **WHEN** an Owner saves an OpenAI key
- **THEN** the configured OpenAI models become available only to that Owner
- **AND** the API accepts no arbitrary provider endpoint or unconfigured model

#### Scenario: Colliding native model names
- **WHEN** two providers configure the same native model ID
- **THEN** their selection IDs differ and each resolves to the correct provider

### Requirement: Effective default model
A user's effective default SHALL be their stored selection when it is in their allowed list. Otherwise it SHALL be the configured Gemini default, using the current user's personal Gemini key when present or the shared key when absent. Changes in credentials SHALL update routing metadata without changing existing Gemini selection IDs.

#### Scenario: No stored default
- **WHEN** the Owner has never set a default
- **THEN** the effective default is the configured Gemini default

#### Scenario: Stored default removed from config
- **WHEN** the Owner's stored default is no longer configured
- **THEN** the effective default is the configured Gemini default and no error is returned

#### Scenario: Key removed for default provider
- **WHEN** the Owner removes the OpenAI key for their stored default
- **THEN** that selection becomes unavailable and the effective default is configured Gemini

### Requirement: Set default model
`setDefaultLlmModel(model)` SHALL store the allowed selection ID and its provider as the current user's default, and SHALL clear the stored default when `model` is null. It SHALL return the updated `llmModels` result and SHALL only change the current user's setting.

#### Scenario: Owner sets a default
- **WHEN** the Owner sets an allowed model as default
- **THEN** it becomes that Owner's effective default and the Guest's default is unchanged

#### Scenario: Clear the default
- **WHEN** the Owner calls `setDefaultLlmModel` with null
- **THEN** the effective default is configured Gemini with its resolved key source

#### Scenario: Model not allowed
- **WHEN** the Guest sets a configured model outside the Guest's allowed list
- **THEN** `UnknownLlmModelError` is returned and the stored setting is unchanged

### Requirement: Model per AI call
`parseListing`, `parseListingFromUrl`, `parseUrl`, `parseProfileCv`, `analyzeTailoredCvSection` and `analyzeAllTailoredCv` SHALL accept optional `model` as a selection ID. Each SHALL use that selection when given, otherwise the effective default, and resolve provider/native model/credential before calling AI. Usage SHALL record that resolved attribution; parse jobs SHALL record provider and native model. Raw credentials SHALL never be stored on jobs.

#### Scenario: Override for one call
- **WHEN** the Owner's default is Gemini A and the Owner parses a listing with an allowed OpenAI selection B
- **THEN** the provider receives B's native model ID using the Owner's OpenAI key
- **AND** the parse job and usage record the actual provider and native model

#### Scenario: No override
- **WHEN** the Owner parses a CV without `model` and their default is A
- **THEN** the AI call and its usage use A's resolved provider/model/credential

#### Scenario: Full tailored CV analysis
- **WHEN** the Owner analyzes all sections using a personal provider
- **THEN** every attempted section call is attributed to its selected credential
- **AND** provider failure never switches credentials automatically

### Requirement: Unknown model rejected
An AI mutation whose selection ID is not in the current user's allowed list, compared exactly, SHALL return `UnknownLlmModelError`. It SHALL make no AI call or URL fetch and store no parse job or usage row. The error SHALL NOT echo the submitted value.

#### Scenario: Guest requests another model
- **WHEN** the Guest calls `parseListing` with a configured nondefault model
- **THEN** `UnknownLlmModelError` is returned with no AI call, parse job, or usage row

#### Scenario: Arbitrary model id
- **WHEN** the Owner calls `analyzeTailoredCvSection` with an unconfigured selection ID
- **THEN** `UnknownLlmModelError` is returned and no AI call is made

#### Scenario: Different case
- **WHEN** the Owner sends a configured selection ID in different letter case
- **THEN** `UnknownLlmModelError` is returned

#### Scenario: Provider key no longer configured
- **WHEN** the Owner explicitly requests an OpenAI selection after removing its key
- **THEN** the selection is rejected without an AI call or implicit Gemini fallback
