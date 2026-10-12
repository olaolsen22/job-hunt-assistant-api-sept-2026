# Spec Delta

## Purpose

Defines validation guarantees for external tailored CV candidates so source-attributed content remains trustworthy and malformed input returns reviewable errors. Candidates remain transient until the user explicitly saves a reviewed draft.

## ADDED Requirements

### Requirement: External source bullets preserve their source text
When an external candidate attributes a bullet to a profile source bullet, the candidate text SHALL match that source bullet's text after the established whitespace normalization. A mismatch SHALL return a readable validation error and no candidate data. User-edited draft content saved through the draft save operation may differ from source text.

#### Scenario: Exact source bullet
- **WHEN** an external candidate includes a source bullet ID and the matching profile bullet text
- **THEN** the preview returns the candidate for review

#### Scenario: Altered source bullet
- **WHEN** an external candidate includes a valid source bullet ID but different text
- **THEN** the preview returns a readable validation error and no candidate

#### Scenario: User edits a source bullet in a draft
- **WHEN** a user saves draft text edited from a source bullet while retaining its source ID
- **THEN** the draft save accepts the edited text and preserves its source attribution

### Requirement: Malformed external candidate arrays return validation errors
The external preview SHALL reject null items in candidate arrays at any supported nesting level with a readable validation error. It SHALL return no candidate and SHALL NOT persist draft data.

#### Scenario: Null section item
- **WHEN** pasted candidate JSON contains a null experience, education, certification, project, skill, language, or link item
- **THEN** the preview returns a readable validation error and no candidate

#### Scenario: Null nested bullet
- **WHEN** pasted candidate JSON contains a null bullet item within an entry
- **THEN** the preview returns a readable validation error and no candidate

#### Scenario: Valid candidate after invalid preview
- **WHEN** the user submits a valid candidate after a null-item candidate was rejected
- **THEN** the valid candidate can be previewed and neither preview has created a draft
