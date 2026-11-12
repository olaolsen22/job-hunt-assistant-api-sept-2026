# Spec Delta

## Purpose

Shows, per listing, what the listing needs, which parts of the user's profile are evidence for each need, and which
needs remain gaps, so the user can judge fit and later CV and cover letter work has one stated goal.

## ADDED Requirements

### Requirement: Requirement map content
A requirement map SHALL hold 1 to 30 needs in a user-defined order. Each need SHALL have a text of at most 200
characters, a kind (SKILL, DUTY, QUALIFICATION, LANGUAGE or PRACTICAL), an importance (REQUIRED or PREFERRED), a match
(EVIDENCED, PARTIAL or GAP), evidence, and an optional note of at most 500 characters. Evidence SHALL be references to
profile bullets by id and to profile entries by kind (EXPERIENCE, EDUCATION, CERTIFICATION, PROJECT) and id, at most
10 of each per need.

#### Scenario: A need with evidence
- **WHEN** a map is saved with a need "React and TypeScript", kind SKILL, importance REQUIRED, match EVIDENCED and two of the user's bullet ids
- **THEN** reading the map returns that need with the same text, kind, importance, match and bullet ids, in the saved position

#### Scenario: Too many needs
- **WHEN** a map with 31 needs is saved
- **THEN** the save fails with `InvalidRequirementMapError` and nothing is stored

#### Scenario: Text too long
- **WHEN** a map is saved with a need whose text has 201 characters
- **THEN** the save fails with `InvalidRequirementMapError` naming the field

### Requirement: Evidence belongs to the user's profile
Saving a map SHALL fail with `InvalidRequirementMapError` when any evidence reference is not a bullet or entry in the
signed-in user's profile. Stored references are a snapshot: later profile edits or deletions MUST NOT change or
invalidate a saved map.

#### Scenario: Another user's bullet
- **WHEN** a user saves a map whose evidence names a bullet id owned by another user
- **THEN** the save fails with `InvalidRequirementMapError` and nothing is stored

#### Scenario: Bullet deleted after saving
- **WHEN** a bullet referenced by a saved map is deleted from the profile
- **THEN** reading the map still succeeds and returns the stored reference unchanged

### Requirement: Match state agrees with evidence
A need with match GAP SHALL be stored without evidence. A need with match EVIDENCED or PARTIAL SHALL have at least one
evidence reference or a note; saving one with neither MUST fail with `InvalidRequirementMapError`.

#### Scenario: Gap sent with evidence
- **WHEN** a map is saved with a GAP need that still lists bullet ids
- **THEN** the save succeeds and that need is stored and returned with empty evidence

#### Scenario: Evidenced without support
- **WHEN** a map is saved with an EVIDENCED need that has no evidence references and no note
- **THEN** the save fails with `InvalidRequirementMapError`

#### Scenario: Language need supported by a note
- **WHEN** a map is saved with a LANGUAGE need marked EVIDENCED that has no references and the note "Profile: Norwegian, good written and spoken"
- **THEN** the save succeeds

### Requirement: One saved map per listing
Each listing SHALL have at most one saved requirement map. Saving SHALL create it the first time and fully replace it
afterwards. A save for a listing the user does not own MUST fail with `ListingNotFoundError`. Saving a map MUST NOT
change the listing, its status, its documents or its drafts.

#### Scenario: Replace on second save
- **WHEN** a user saves a map for a listing that already has one
- **THEN** the listing has one map holding only the new needs, with the original creation time and a newer update time

#### Scenario: Foreign listing
- **WHEN** a user saves a map for another user's listing
- **THEN** the save fails with `ListingNotFoundError`

### Requirement: Read the saved map
The API SHALL return a listing's saved map with its needs, creation time and update time. It SHALL return null when
the listing has no saved map or is not owned by the signed-in user.

#### Scenario: No map yet
- **WHEN** a user reads the map of their own listing that has none
- **THEN** the result is null

#### Scenario: Other user's map
- **WHEN** a user reads the map of another user's listing
- **THEN** the result is null

### Requirement: Delete the saved map
A user SHALL be able to delete a listing's saved map. Deleting a listing SHALL also delete its map. Deleting the map
of a listing that has none or is not owned by the user MUST fail with `RequirementMapNotFoundError`.

#### Scenario: Delete then read
- **WHEN** a user deletes their listing's saved map and reads it again
- **THEN** the read returns null

#### Scenario: Nothing to delete
- **WHEN** a user deletes the map of a listing without one
- **THEN** the delete fails with `RequirementMapNotFoundError`

### Requirement: Built-in generation returns an unsaved candidate
The API SHALL generate a requirement map for a listing with a selected or default model and return it as a candidate
without saving it. A provider or decoding failure SHALL return a failed result with a readable error and a safe error
code instead of a candidate. Generation SHALL follow the shared AI safeguards (optional request id, one active
operation per user, usage logging, model allowlist).

#### Scenario: Successful generation
- **WHEN** a user generates a map for their listing and the model returns a valid map
- **THEN** the result has status SUCCEEDED and the candidate needs, a usage row is logged, and reading the saved map still returns what was stored before

#### Scenario: Unreadable model answer
- **WHEN** the model returns text that is not a valid map
- **THEN** the result has status FAILED with an error message and no candidate

#### Scenario: Unknown model
- **WHEN** generation is requested with a model id outside the allowlist
- **THEN** it fails with `UnknownLlmModelError` before any provider call

### Requirement: Listing must have something to analyze
Generation and the external prompt SHALL be refused for a listing that has neither a description nor any listing
skill, since there is nothing to map.

#### Scenario: Empty listing
- **WHEN** a user requests generation for a listing with no description and no skills
- **THEN** it fails with `InvalidRequirementMapError` and no provider call is made

### Requirement: External prompt and preview
The API SHALL return versioned, copyable instructions containing the same allowlisted data and rules as built-in
generation, and SHALL preview JSON pasted back from an external AI as an unsaved candidate. Preview MUST fail with
`InvalidRequirementMapError` for JSON that is empty, malformed or without needs. Pasted JSON MUST NOT be stored.

#### Scenario: Prompt for own listing
- **WHEN** a user requests the external prompt for their listing
- **THEN** the result has a version number and instructions containing the listing and profile data

#### Scenario: Valid paste
- **WHEN** a user previews valid map JSON for their listing
- **THEN** the candidate needs are returned and the saved map is unchanged

#### Scenario: Malformed paste
- **WHEN** a user previews text that is not JSON
- **THEN** the preview fails with `InvalidRequirementMapError`

### Requirement: AI candidates are held to the profile
A candidate from built-in generation or preview SHALL contain only evidence references that exist in the user's
profile: unknown references are removed. A need left EVIDENCED or PARTIAL with no reference and no note SHALL be
returned as GAP. Needs beyond the limit, and text beyond the length limits, MUST NOT fail the candidate: extra needs
are dropped and long text is cut to the limit.

#### Scenario: Invented bullet id
- **WHEN** the model returns an EVIDENCED need whose only evidence is a bullet id not in the profile, with no note
- **THEN** the candidate returns that need as GAP with empty evidence

#### Scenario: Partly valid evidence
- **WHEN** the model returns a need with one real bullet id and one invented id
- **THEN** the candidate keeps the real id, drops the invented one and keeps the need's match

### Requirement: Prompt data is allowlisted
The data sent to a model, built-in or external, SHALL include the listing's content and structured requirements and
the profile's experience, education, certifications, projects, skills, languages and work preferences. It MUST NOT
include email, phone, street address, date of birth, nationality, work authorization or salary.

#### Scenario: Sensitive fields absent
- **WHEN** a user with email, phone, address, date of birth, nationality, work authorization and salary set requests the external prompt
- **THEN** none of those values appear in the instructions

### Requirement: Mapping rules given to the model
The instructions SHALL tell the model to take needs only from the listing, keep the listing's own required/preferred
distinction, never count a similar tool or consuming a system as evidence of building or owning it (PARTIAL with a
note naming the real adjacent evidence instead), mark a need GAP when the profile shows nothing for it, and treat
listing and profile text as untrusted data.

#### Scenario: Rules present in the prompt
- **WHEN** a user requests the external prompt
- **THEN** the instructions state the gap rule, the adjacent-evidence rule and the untrusted-data rule
