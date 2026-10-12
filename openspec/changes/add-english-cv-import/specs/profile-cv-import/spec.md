# Spec Delta

## Purpose

Extracts English CV content into reviewable candidates and lets users selectively import it into their master profile at any time through built-in or external AI.

## ADDED Requirements

### Requirement: Built-in text parsing
`parseProfileCv(text)` SHALL accept nonblank English plain text or Markdown up to 100,000 characters, send the complete accepted text to the configured built-in AI, and return a succeeded candidate or failed result. It SHALL NOT change profile data or lookups. Blank or oversized input SHALL return `InvalidParseRequestError` without an AI call. File input SHALL NOT be supported.

#### Scenario: Markdown input
- **WHEN** the user parses an English Markdown CV
- **THEN** all accepted CV text reaches the AI and proposed content is returned without saving it

#### Scenario: Input too long
- **WHEN** the user submits 100,001 characters
- **THEN** the operation returns InvalidParseRequestError and makes no AI call

### Requirement: External prompt without CV input
`profileCvParsePrompt` SHALL return versioned instructions without requiring CV text, a file, or an external API key. Built-in parsing SHALL use the same instructions. The prompt SHALL tell the external AI to use the CV supplied directly by the user and return the shared English candidate format. Prompt retrieval SHALL NOT call an AI or log AI usage.

#### Scenario: Copy prompt
- **WHEN** the user requests the external prompt
- **THEN** instructions and their version are returned without sending any CV or existing profile content to an AI

### Requirement: Shared candidate format
Both paths SHALL produce versioned English candidates with basics, links, hobbies, experiences, educations, certifications, projects, skills, languages, and warnings. Unknown values SHALL remain null or omitted; source wording SHALL be preserved without invented achievements. Sensitive personal details, references, and job preferences SHALL be excluded. Unknown skill or language levels SHALL NOT be invented.

#### Scenario: Incomplete dates and proficiency
- **WHEN** the CV lists an experience without a start month and a skill without a proficiency level
- **THEN** both appear in the candidate with missing values and warnings for review rather than invented dates or levels

### Requirement: Read-only JSON preview
`previewProfileCvImport(json)` SHALL decode the shared candidate format and return structured candidates with field-specific review warnings. Invalid JSON, unsupported versions, wrong shapes, or input over 200,000 characters SHALL return `InvalidProfileImportError`. It SHALL NOT change profile data, create lookups, or call an AI. Existing profile content SHALL remain available through the profile query for review.

#### Scenario: External result
- **WHEN** the user previews valid JSON returned by an external AI
- **THEN** the structured candidate is returned with the same shape as built-in parsing

#### Scenario: Wrong result shape
- **WHEN** the user previews a JSON array instead of the candidate object
- **THEN** InvalidProfileImportError is returned and no data changes

### Requirement: Explicit selective save
`importProfileCv(input)` SHALL save only reviewed selections into the signed-in user's profile and return the profile. Omitted basic fields SHALL remain unchanged. Explicit null SHALL clear an optional selected field. Included links and hobbies SHALL replace those lists only when explicitly supplied. Entries SHALL be added without an id or updated by an explicit existing id. Unselected entries and preferences SHALL remain unchanged.

#### Scenario: Import after manual editing
- **WHEN** a populated profile imports a new project and a selected headline
- **THEN** the project is added and the headline updated while existing experiences, preferences, and other basics remain unchanged

#### Scenario: Deliberate clear
- **WHEN** the user imports an explicit null phone and omits email
- **THEN** phone is cleared and email remains unchanged

### Requirement: Preserve entry and bullet identities
Updating an entry through import SHALL preserve its id and existing bullets. Supplied bullets SHALL append without an id or update an explicit bullet id belonging to that entry. Omitted bullets SHALL remain unchanged; import SHALL NOT delete or reorder existing bullets. New bullets SHALL append in request order. Updating source text SHALL preserve translations and let existing staleness rules apply.

#### Scenario: Update one translated bullet
- **WHEN** import updates an existing bullet text on an existing experience
- **THEN** entry and bullet ids remain unchanged, other bullets remain, and the existing translation becomes stale if its source differs

### Requirement: Validation and atomicity
Selected values SHALL satisfy existing profile, entry, bullet, location, skill, language, and link rules. Missing required dates or levels SHALL prevent saving the affected selection until corrected. Any invalid selection SHALL fail the entire import without saving profile or lookup changes. Duplicate target ids in one request SHALL be rejected. Errors SHALL identify the invalid selection.

#### Scenario: Invalid final selection
- **WHEN** valid basics and a project precede an invalid experience in one import
- **THEN** the whole import fails and no basics, projects, or lookups are saved

#### Scenario: Missing skill level
- **WHEN** the user selects a standalone skill with no level
- **THEN** the import fails with a reviewable error; existing levels are never guessed or overwritten implicitly

### Requirement: User isolation
All explicit target ids SHALL belong to the signed-in user's profile and the stated entry kind. A bullet id SHALL belong to its selected parent entry. Foreign or unknown ids SHALL return ProfileEntryNotFoundError without changes. Lookups SHALL be reused or created only within the signed-in user's scope.

#### Scenario: Foreign target
- **WHEN** the guest imports an update targeting an owner's experience
- **THEN** ProfileEntryNotFoundError is returned and neither user's profile changes

### Requirement: English primary content
Saving SHALL require the profile's contentLanguage to be ENGLISH and the reviewed candidate content language to be ENGLISH. Import SHALL NOT change contentLanguage, write translations, or delete existing translations. Parsing and prompt retrieval SHALL remain available for a profile whose primary language is Norwegian.

#### Scenario: Norwegian primary profile
- **WHEN** the user saves an English import into a Norwegian primary profile
- **THEN** InvalidProfileImportError explains the English-only limitation and the profile remains unchanged

### Requirement: AI failures and usage
Each built-in AI call SHALL log usage with an operation distinct from listing parsing, provider, model, success, and available token counts. Invalid AI output, provider failure, rate limiting, or timeout SHALL return a readable failed result without profile writes. Raw CV text SHALL NOT be retained in a persistent parse job or logged by this feature. External prompt and preview SHALL create no AI usage rows.

#### Scenario: Provider rate limit
- **WHEN** the built-in provider responds with rate limiting
- **THEN** parsing returns a failed result and unsuccessful usage record, with no profile changes or retained CV text
