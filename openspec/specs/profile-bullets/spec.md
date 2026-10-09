# profile-bullets Specification

## Purpose
Free text on a profile lives in bullets: short lines under an experience, education, certification, or project. Each
bullet has its own id that survives edits and reordering, so something outside the profile can later refer to it.

## Requirements

### Requirement: Bullets belong to one entry
Every profile experience, education, certification, and project SHALL expose `bullets`, ordered by position ascending.
A bullet SHALL belong to exactly one entry, and SHALL be deleted when its entry is deleted. A new entry SHALL have no
bullets.

#### Scenario: New entry has no bullets
- **WHEN** the user adds an experience
- **THEN** its `bullets` list is empty

#### Scenario: Bullets follow their entry
- **WHEN** the user deletes an education entry that has three bullets
- **THEN** the three bullets no longer exist, and bullets on other entries are unchanged

#### Scenario: Each kind has bullets
- **WHEN** the user adds a bullet to an experience, a certification, and a project
- **THEN** each entry lists its own bullet and no other

### Requirement: Add a bullet
`addProfileBullet` SHALL accept `{kind, entryId, text}` with `kind` one of EXPERIENCE, EDUCATION, CERTIFICATION,
PROJECT, and SHALL return the bullet. The new bullet SHALL be placed last in its entry. Text SHALL be trimmed. An
unknown entry id for that kind, or another user's, SHALL fail with `ProfileEntryNotFoundError`.

#### Scenario: Append
- **WHEN** an experience has bullets A and B and the user adds C
- **THEN** the experience lists A, B, C and C has position 2

#### Scenario: Wrong kind
- **WHEN** the user adds a bullet with kind EDUCATION and the id of an experience
- **THEN** the request fails with `ProfileEntryNotFoundError`

#### Scenario: Foreign entry
- **WHEN** the guest adds a bullet to an experience that belongs to the owner
- **THEN** the request fails with `ProfileEntryNotFoundError` and the owner's experience is unchanged

### Requirement: Update a bullet
`updateProfileBullet` SHALL accept `{id, text}`, replace the text (trimmed), and return the bullet. The bullet SHALL
keep its id, entry, and position. An unknown bullet id, or another user's, SHALL fail with `ProfileEntryNotFoundError`.

#### Scenario: Edit keeps identity
- **WHEN** the user changes a bullet's text
- **THEN** the returned bullet has the same id and position and the new text

#### Scenario: Unknown bullet
- **WHEN** the user updates a bullet id that does not exist
- **THEN** the request fails with `ProfileEntryNotFoundError`

### Requirement: Move a bullet
`moveProfileBullet` SHALL accept `{id, position}`, put the bullet at that zero-based position among its entry's
bullets, and return the entry's bullets in their new order. Positions SHALL stay consecutive from 0, with the other
bullets keeping their relative order. A position below 0 or beyond the last SHALL fail with `InvalidProfileError`.
An unknown bullet SHALL fail with `ProfileEntryNotFoundError`.

#### Scenario: Move up
- **WHEN** an entry has bullets A, B, C and the user moves C to position 0
- **THEN** the returned bullets are C, A, B with positions 0, 1, 2

#### Scenario: Move to the end
- **WHEN** an entry has bullets A, B, C and the user moves A to position 2
- **THEN** the returned bullets are B, C, A

#### Scenario: Out of range
- **WHEN** an entry has three bullets and the user moves one to position 3
- **THEN** the request fails with `InvalidProfileError` and the order is unchanged

### Requirement: Delete a bullet
`deleteProfileBullet` SHALL accept `{id}`, delete the bullet, and return the id. The entry's remaining bullets SHALL
keep their relative order with positions consecutive from 0. An unknown bullet SHALL fail with
`ProfileEntryNotFoundError`.

#### Scenario: Delete closes the gap
- **WHEN** an entry has bullets A, B, C and the user deletes B
- **THEN** the entry lists A, C with positions 0 and 1

### Requirement: Bullet validation
Bullet text SHALL be non-blank and at most 500 characters after trimming. An entry SHALL have at most 20 bullets.
Violations SHALL fail with `InvalidProfileError` and change nothing.

#### Scenario: Blank text
- **WHEN** the user adds a bullet with text `"   "`
- **THEN** the request fails with `InvalidProfileError`

#### Scenario: Too long
- **WHEN** the user adds a bullet of 501 characters
- **THEN** the request fails with `InvalidProfileError`

#### Scenario: Too many
- **WHEN** an entry has 20 bullets and the user adds one more
- **THEN** the request fails with `InvalidProfileError` and the entry still has 20

### Requirement: Entries have no description or highlights
Experience, education, and project entries SHALL NOT have a `description`, and experience SHALL NOT have
`highlights`, in the schema or the add and update mutations. Updating an entry SHALL NOT change its bullets. Bullets
are the only free text on an entry. The profile `summary` is unchanged.

#### Scenario: Update leaves bullets
- **WHEN** the user updates an experience's title
- **THEN** its bullets are unchanged, with the same ids

#### Scenario: Old fields are gone
- **WHEN** a client queries `description` on an experience, or `highlights`
- **THEN** the query is rejected as an unknown field

### Requirement: Existing text becomes bullets
When the feature is introduced, each non-blank description SHALL become the entry's first bullet, followed by each
highlight in its existing order, so no text is lost. Description text longer than the bullet limit SHALL be kept
whole rather than cut.

#### Scenario: Description and highlights migrated
- **WHEN** an experience had description "Built the dashboard" and highlights ["Led migration", "Cut load time 40%"]
- **THEN** it has the bullets "Built the dashboard", "Led migration", "Cut load time 40%" at positions 0, 1, 2

#### Scenario: Blank text skipped
- **WHEN** an entry had no description and no highlights
- **THEN** it has no bullets
