# profile-content-language Specification

## Purpose
Records which language the user writes their profile text in, so translations know what they are translations of
and can never be made into the language the text is already in.

## Requirements

### Requirement: Content language
The profile SHALL have a `contentLanguage`, `ENGLISH` or `NORWEGIAN`, which is `ENGLISH` for every existing and new
profile. `setProfileContentLanguage` SHALL change it.

#### Scenario: Default
- **WHEN** a profile exists that was never given a content language
- **THEN** its `contentLanguage` is `ENGLISH`

#### Scenario: Change with no translations
- **WHEN** the user sets the content language to `NORWEGIAN` and no translation exists
- **THEN** the profile returns `NORWEGIAN`

### Requirement: Content language is fixed while translations exist
`setProfileContentLanguage` SHALL return `InvalidProfileError` and change nothing if any bullet translation or profile
translation exists, because their source text and language would no longer match. Setting the language it already has
SHALL succeed.

#### Scenario: Blocked by a translation
- **WHEN** a bullet has a Norwegian translation and the user sets the content language to `NORWEGIAN`
- **THEN** the call returns `InvalidProfileError` and the content language is unchanged

#### Scenario: Same value
- **WHEN** translations exist and the user sets the language it already has
- **THEN** the call succeeds and nothing changes
