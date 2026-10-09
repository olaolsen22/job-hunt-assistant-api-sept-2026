# Spec Delta

## Purpose

People who can vouch for the user, kept as an ordered list on the profile so a CV can show some or all of them.

## ADDED Requirements

### Requirement: References
The profile SHALL list `references` ordered by position. Each has a required `name` and `relationship`, and optional
`company`, `email`, `phone`, and `note`. Text SHALL be trimmed and limited to 200 characters (`note` 500), `email`
SHALL be a valid address, and a profile SHALL have at most 10 references. Violations SHALL return `InvalidProfileError`.

#### Scenario: Add appends
- **WHEN** the user adds two references
- **THEN** `references` lists them in the order added, with positions 0 and 1

#### Scenario: Missing relationship
- **WHEN** the user adds a reference with a blank relationship
- **THEN** the call returns `InvalidProfileError` and nothing is stored

### Requirement: Edit, move and delete references
`updateProfileReference` SHALL replace a reference's fields and keep its id and position. `moveProfileReference` SHALL
move it to a new 0-based position and renumber the rest consecutively. `deleteProfileReference` SHALL remove it and close
the gap. An unknown id, or one owned by another user, SHALL return `ProfileEntryNotFoundError`; a position out of range
SHALL return `InvalidProfileError` with the order unchanged.

#### Scenario: Move to the front
- **WHEN** the user moves the third of three references to position 0
- **THEN** the order is third, first, second with positions 0, 1, 2

#### Scenario: Delete closes the gap
- **WHEN** the user deletes the middle of three references
- **THEN** the remaining two have positions 0 and 1

#### Scenario: Foreign reference
- **WHEN** a user updates a reference id that belongs to another user
- **THEN** the call returns `ProfileEntryNotFoundError`
