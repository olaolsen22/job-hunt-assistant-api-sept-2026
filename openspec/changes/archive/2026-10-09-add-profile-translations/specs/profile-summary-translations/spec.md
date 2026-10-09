# Spec Delta

## Purpose

The profile headline and summary can be translated into the other language, with the same stale tracking as bullets.

## ADDED Requirements

### Requirement: Set a profile translation
`setProfileTranslation` SHALL create or replace, as a whole, the translation of the headline and summary in a given
language, so a field left out is cleared. Each given field SHALL be trimmed and limited like its source field, and SHALL
require the profile to have that source field. At least one field SHALL be given; otherwise, or for a language equal to
`contentLanguage`, the call SHALL return `InvalidProfileError`. The current source headline and summary SHALL be stored
with it, and `isMachine` SHALL default to false. A profile SHALL have at most one translation per language.

#### Scenario: Translate both
- **WHEN** the user sets a Norwegian headline and summary
- **THEN** the profile lists one `NORWEGIAN` translation with both, and neither is stale

#### Scenario: Replace clears a field
- **WHEN** the user sets the Norwegian translation again with only a headline
- **THEN** the summary translation is null

#### Scenario: No source
- **WHEN** the profile has no summary and the user sends a summary translation
- **THEN** the call returns `InvalidProfileError`

#### Scenario: Nothing given
- **WHEN** the user sends neither headline nor summary
- **THEN** the call returns `InvalidProfileError`

### Requirement: Stale profile translations
A profile translation SHALL expose `isHeadlineStale` and `isSummaryStale`. Each is true exactly when that translated
field is present and the profile's current headline or summary differs from the copy stored with it, and false when the
translated field is absent. Editing the profile SHALL NOT change or delete translations.

#### Scenario: Summary edited
- **WHEN** the user updates the profile summary after translating headline and summary
- **THEN** `isSummaryStale` is true and `isHeadlineStale` is false

### Requirement: Delete a profile translation
`deleteProfileTranslation` SHALL remove the translation of one language, returning `ProfileEntryNotFoundError` if there
is none. Deleting the profile SHALL delete its translations.

#### Scenario: Delete
- **WHEN** the user deletes the Norwegian translation
- **THEN** the profile's `translations` no longer lists it
