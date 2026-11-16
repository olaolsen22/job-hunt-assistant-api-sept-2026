# llm-provider-keys Specification

## Purpose

Lets authenticated real users manage private AI provider credentials and run AI actions with their own keys while preserving the shared Gemini fallback and credential-specific usage history.

## Requirements

### Requirement: Private provider key management
Owners SHALL manage one active personal key per provider through `setProviderKey(provider, apiKey)` and `removeProviderKey(provider)`. Supported providers SHALL be Gemini, OpenAI, and Mistral. Guest and non-Owner callers SHALL be denied before validation requests or writes. Operations SHALL affect only the current user's keys.

#### Scenario: Set a personal key
- **WHEN** an Owner saves a valid key for a supported provider
- **THEN** that key becomes the Owner's active key for that provider
- **AND** another Owner's key and the shared configuration are unchanged

#### Scenario: Guest denied
- **WHEN** the Guest attempts to set or remove a personal key
- **THEN** the mutation returns a safe authorization error without provider requests or writes

### Requirement: Safe credential status
`providerKeys(includeRetired: false)` SHALL return safe status for supported providers and the current user's credentials: credential ID, provider, configured status, last four characters when configured, and lifecycle timestamps. It SHALL NOT return plaintext or encrypted keys. Guests SHALL have no personal credentials exposed. Anonymous callers SHALL be rejected.

#### Scenario: Query safe status
- **WHEN** an Owner queries provider keys after configuring Gemini
- **THEN** Gemini reports a configured credential and its last four characters
- **AND** other supported providers report unconfigured status and no secret material is returned

#### Scenario: Different user's key
- **WHEN** another Owner queries provider keys
- **THEN** none of the first Owner's credential metadata is returned

#### Scenario: Removed provider
- **WHEN** an Owner has a stored credential for a provider that is no longer supported
- **THEN** key status queries omit that credential, including when retired credentials are requested
- **AND** attempts to configure that provider are rejected before an outbound request

### Requirement: Validate before replacing a key
Key save SHALL reject unsupported providers, blank keys, and keys over 4,096 characters before outbound requests. Supported keys SHALL pass a bounded authenticated provider model-list request before persistence. Validation failure or cancellation SHALL preserve the previous active key. Successful validation SHALL NOT imply model access, inference quota, or available credit.

#### Scenario: Invalid replacement
- **WHEN** an Owner replaces a working key and the provider rejects the new credential
- **THEN** a safe credential error is returned and the working key remains active

#### Scenario: Temporary validation failure
- **WHEN** validation encounters rate limiting, timeout, or provider unavailability
- **THEN** a safe transient error is returned without changing the active key

### Requirement: Durable protected credentials
Saved personal keys SHALL be encrypted at rest and decryptable across normal application restarts and container recreation with the persisted encryption key ring. Credential storage and backups SHALL NOT expose plaintext keys. A key that cannot be decrypted SHALL produce a safe credential-unavailable error rather than using the shared key.

#### Scenario: Recreate application
- **WHEN** a configured application is recreated using its existing database and encryption key ring
- **THEN** previously saved keys remain usable

#### Scenario: Missing encryption keys
- **WHEN** a stored personal key cannot be decrypted
- **THEN** the AI operation fails safely and makes no shared-key call

### Requirement: Credential history survives removal
Each accepted replacement SHALL receive a new credential ID. Replaced and removed credentials SHALL retain only safe historical metadata and usage attribution; their stored secret SHALL be erased. Removal with no active key SHALL succeed without changes. Historical usage SHALL remain distinguishable by credential ID.

#### Scenario: Replace a key
- **WHEN** an Owner replaces a key that has recorded usage
- **THEN** new calls use a new credential ID and old usage remains attributed to the retired credential
- **AND** the retired secret is erased

#### Scenario: Remove a key
- **WHEN** an Owner removes a configured personal key
- **THEN** it cannot serve subsequent calls and its historical usage remains readable only by its owner

### Requirement: Shared fallback only for absent Gemini key
An allowed Gemini selection SHALL use the caller's active personal Gemini key when present, otherwise the configured shared Gemini key. Other providers SHALL require the caller's active personal key. A rejected, rate-limited, unavailable, or undecryptable personal key SHALL NOT cause automatic calls with another key or provider.

#### Scenario: No personal Gemini key
- **WHEN** a user runs an allowed Gemini model without a personal Gemini key
- **THEN** exactly one call is attempted with the shared Gemini key

#### Scenario: Personal key fails
- **WHEN** an AI call using a personal key fails
- **THEN** the normal operation returns a safe failure and no shared or alternative-provider call is attempted

#### Scenario: Another provider is configured
- **WHEN** an Owner has an OpenAI key but no Gemini key and selects Gemini
- **THEN** the selected Gemini call uses the shared key

### Requirement: Provider secrets stay out of diagnostics
Provider errors SHALL use stable safe codes and messages for authentication, rate limiting, timeout, and other provider failures. API responses and application logs SHALL NOT contain keys, authorization headers, raw provider response bodies, or submitted CV/listing text. Caller cancellation SHALL retain its cancellation semantics.

#### Scenario: Exception contains a credential
- **WHEN** a provider exception includes a submitted key or request headers
- **THEN** neither the returned error nor application diagnostics contain that material

