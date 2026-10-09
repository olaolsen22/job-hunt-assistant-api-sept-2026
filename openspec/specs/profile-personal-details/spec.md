# profile-personal-details Specification

## Purpose
Personal details a CV commonly shows beyond name and contact: date of birth, nationality, work authorization,
hobbies, and typed links such as GitHub and LinkedIn. All optional, edited with the rest of the profile basics.

## Requirements

### Requirement: Sensitive personal details
The profile SHALL store an optional `dateOfBirth`, `nationality`, and `workAuthorization`. `updateProfile` SHALL
replace them like the other basics, so a value left out is cleared. A date of birth in the future SHALL be rejected
with an `InvalidProfileError`. Text SHALL be trimmed and limited to 200 characters, and blank text SHALL be cleared.

#### Scenario: Set and clear
- **WHEN** the user calls `updateProfile` with a date of birth and nationality, then again without them
- **THEN** the first call returns both values and the second returns null for both

#### Scenario: Future date of birth
- **WHEN** the user sends a date of birth after today
- **THEN** the call returns `InvalidProfileError` and the profile is unchanged

### Requirement: Hobbies
The profile SHALL store `hobbies` as an ordered list of text, replaced as a whole by `updateProfile`. Entries SHALL be
trimmed, blank entries dropped, duplicates (ignoring case) dropped, and the list limited to 20 entries of at most 100
characters.

#### Scenario: Replace hobbies
- **WHEN** the user sends `["Climbing", " climbing ", "", "Chess"]`
- **THEN** the profile's hobbies are `["Climbing", "Chess"]`

#### Scenario: Too many hobbies
- **WHEN** the user sends 21 hobbies
- **THEN** the call returns `InvalidProfileError`

### Requirement: Typed links
Each profile link SHALL have a `kind` of `GITHUB`, `LINKEDIN`, `PORTFOLIO`, or `OTHER`, and a `url`. `label` SHALL be
required for `OTHER` and ignored (stored empty) for the other kinds. A `GITHUB` link SHALL be on `github.com` and a
`LINKEDIN` link on `linkedin.com`, each including subdomains such as `www.`; otherwise the call SHALL return
`InvalidProfileError`. At most one link per kind SHALL be allowed except `OTHER`.

#### Scenario: GitHub link
- **WHEN** the user sends kind `GITHUB` with `https://github.com/ola`
- **THEN** the link is stored with that kind and no label

#### Scenario: Wrong host
- **WHEN** the user sends kind `LINKEDIN` with `https://example.com/ola`
- **THEN** the call returns `InvalidProfileError`

#### Scenario: Other needs a label
- **WHEN** the user sends kind `OTHER` with no label
- **THEN** the call returns `InvalidProfileError`

#### Scenario: Existing links keep working
- **WHEN** the migration runs on a profile with a link labelled "Blog"
- **THEN** the link has kind `OTHER` and label "Blog"
