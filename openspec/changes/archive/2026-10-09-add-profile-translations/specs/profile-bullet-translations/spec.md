# Spec Delta

## Purpose

A bullet can have one translation per language, tied to the bullet by id, so a translation stays attached through
edits and reordering and can be flagged when the original changes after it was made.

## ADDED Requirements

### Requirement: Set a bullet translation
`setProfileBulletTranslation` SHALL create or replace the translation of a bullet in a given language. It SHALL trim the
text, require it to be non-blank and at most 500 characters, and store the bullet's current text as `sourceText`. A
bullet SHALL have at most one translation per language. `isMachine` SHALL default to false. A language equal to
`contentLanguage`, blank or long text SHALL return `InvalidProfileError`; an unknown bullet id, or one owned by another
user, SHALL return `ProfileEntryNotFoundError`.

#### Scenario: Create
- **WHEN** the user translates an English bullet to Norwegian
- **THEN** the bullet lists one `NORWEGIAN` translation with that text, `isMachine` false, and `isStale` false

#### Scenario: Replace
- **WHEN** the user sets a second Norwegian translation for the same bullet
- **THEN** the bullet still lists one Norwegian translation, with the new text, and the same id

#### Scenario: Machine flag
- **WHEN** a translation is saved with `isMachine` true and later replaced by hand without it
- **THEN** its `isMachine` is false

#### Scenario: Same language as content
- **WHEN** the content language is `ENGLISH` and the user sets an `ENGLISH` translation
- **THEN** the call returns `InvalidProfileError`

#### Scenario: Foreign bullet
- **WHEN** a user sets a translation on another user's bullet id
- **THEN** the call returns `ProfileEntryNotFoundError`

### Requirement: Stale translations
Each bullet translation SHALL expose `isStale`, true exactly when the bullet's current text differs from the
translation's `sourceText`. Editing the bullet SHALL NOT change or delete its translations.

#### Scenario: Original edited
- **WHEN** the user edits the bullet text after translating it
- **THEN** the translation is unchanged and its `isStale` is true

#### Scenario: Translation refreshed
- **WHEN** the user then sets the translation again
- **THEN** its `isStale` is false

### Requirement: Delete and cascade
`deleteProfileBulletTranslation` SHALL remove one translation by id, returning `ProfileEntryNotFoundError` for an
unknown or foreign id. Deleting a bullet, or the entry it belongs to, SHALL delete its translations. A bullet's
translations SHALL be listed ordered by language, and moving a bullet SHALL NOT change them.

#### Scenario: Bullet deleted
- **WHEN** the user deletes a bullet that has a translation
- **THEN** the translation no longer exists

#### Scenario: Entry deleted
- **WHEN** the user deletes an experience whose bullet has a translation
- **THEN** the translation no longer exists
