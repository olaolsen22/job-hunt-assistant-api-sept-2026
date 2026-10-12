# Spec Delta

## Purpose

Lets a user tailor profile content to a specific job listing, review AI or external-tool suggestions, and save an editable plain-text CV draft for that application.

## ADDED Requirements

### Requirement: Analyze profile content for a listing
The system SHALL analyze a user's profile content in relation to one of the user's listings, either for a requested section or across the profile as a whole. Analysis SHALL return suggestions for review and SHALL NOT modify the profile or save a CV draft.

#### Scenario: Analyze one experience section
- **WHEN** the user requests analysis for one profile experience on a listing
- **THEN** the result identifies relevant bullets from that experience in relation to the listing
- **AND** no other profile entry is changed

#### Scenario: Analyze the full profile
- **WHEN** the user requests analysis across the profile for a listing
- **THEN** the result contains suggestions for relevant profile sections in relation to that listing
- **AND** no profile entry or saved draft is changed

#### Scenario: Listing belongs to another user
- **WHEN** the user requests analysis for another user's listing
- **THEN** the request is rejected without disclosing listing or profile content

### Requirement: Suggestions refer to profile sources
Analysis suggestions that select existing profile content SHALL identify the source profile item and SHALL NOT invent profile facts. Generated text SHALL be presented as a suggestion and remain distinguishable from selected source text.

#### Scenario: Selected experience bullets
- **WHEN** analysis recommends bullets from an experience
- **THEN** each recommendation identifies its source bullet
- **AND** the user can review the original bullet text

#### Scenario: No relevant bullets
- **WHEN** no bullet in the requested section is relevant to the listing
- **THEN** the result contains no selected bullets for that section

### Requirement: External analysis prompt
The system SHALL provide a copyable prompt and the required JSON shape for external-tool analysis of a listing and profile content. The user SHALL be able to submit the external result as candidate draft data for review without saving it automatically.

#### Scenario: External result populates review data
- **WHEN** the user submits valid JSON returned by an external tool
- **THEN** the system returns validated candidate draft data for the client to display and edit
- **AND** no draft is saved until the user invokes the save operation

#### Scenario: Invalid external JSON
- **WHEN** the submitted external result is malformed or does not match the required shape
- **THEN** the request returns a readable validation error and saves nothing

### Requirement: Review and save a listing-linked draft
The system SHALL allow the user to save reviewed plain-text CV content for a listing. A saved draft SHALL belong to that listing and user, remain separate from uploaded or sent documents, and SHALL NOT be sent by saving.

#### Scenario: Save a reviewed draft
- **WHEN** the user saves edited candidate CV content for their listing
- **THEN** the saved draft contains the submitted plain-text sections and is linked to that listing
- **AND** the listing's application status and sent documents remain unchanged

#### Scenario: Read another user's draft
- **WHEN** a user requests a draft belonging to another user
- **THEN** the system returns no draft content

#### Scenario: Update profile after saving
- **WHEN** the user edits a source profile bullet after saving a draft
- **THEN** the saved draft text remains unchanged until the user edits or saves it again

### Requirement: Analysis uses an explicit profile field allowlist
AI analysis SHALL receive only profile fields needed to select or compose CV content for the listing. Date of birth, nationality, work authorization, contact details, references, and job-search preferences SHALL NOT be included unless a future requirement explicitly permits a field.

#### Scenario: Sensitive fields excluded
- **WHEN** a profile containing sensitive details is analyzed
- **THEN** the analysis input excludes date of birth, nationality, work authorization, contact details, references, and job-search preferences

#### Scenario: External prompt fields
- **WHEN** the system prepares an external-tool prompt
- **THEN** it applies the same profile field allowlist as API-key analysis
