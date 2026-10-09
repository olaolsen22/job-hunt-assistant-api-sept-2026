# profile-history Specification

## Purpose
The dated entries on a profile: work experience, education, certifications, and projects. Each is added, updated, and
deleted on its own.

## Requirements

### Requirement: Month-precision dates
Every entry date (start, end, issued, expires) SHALL be stored and returned as the first day of its month. Any day sent
SHALL be moved to the 1st. A null end date SHALL mean "current" or "ongoing". An end date before the start date SHALL fail
with `InvalidProfileError`.

#### Scenario: Day dropped
- **WHEN** the user adds an experience starting `2021-03-17`
- **THEN** the returned entry starts `2021-03-01`

#### Scenario: End before start
- **WHEN** the user adds an experience starting `2022-05-01` and ending `2021-01-01`
- **THEN** the request fails with `InvalidProfileError`

#### Scenario: Same month allowed
- **WHEN** the user adds an entry starting and ending in `2022-05`
- **THEN** it is saved

### Requirement: Experience entries
`addProfileExperience` / `updateProfileExperience` SHALL accept `{employer, title, startMonth, endMonth?, description?,
highlights, skillNames}` and return the entry. Update fully replaces the entry. `deleteProfileExperience` SHALL
return the deleted id. The employer SHALL be free text and SHALL NOT create or link a company.

#### Scenario: Add experience
- **WHEN** the user adds employer `"Acme AS"`, title `"Frontend Developer"`, start `2020-01`, no end
- **THEN** the profile has that entry with `endMonth: null`, and `companies` has no `"Acme AS"`

#### Scenario: Delete experience
- **WHEN** the user deletes an experience entry
- **THEN** the id is returned and the entry no longer appears on the profile

### Requirement: Entries tag skills by name
Experience and project entries SHALL accept `skillNames`. Each SHALL be found or created in the user's skills
(case-insensitive). Repeats SHALL collapse to one. Tagging a skill SHALL NOT give it a profile level.

#### Scenario: Skill created by tagging
- **WHEN** the user adds an experience tagged `["Vue", "vue", "Nuxt"]` and Nuxt didn't exist
- **THEN** the entry has skills Vue and Nuxt, and `skills` now includes Nuxt

#### Scenario: Update replaces tags
- **WHEN** the user updates an experience tagged Vue and Nuxt with `skillNames: ["React"]`
- **THEN** the entry has only React

### Requirement: Education entries
`addProfileEducation` / `updateProfileEducation` / `deleteProfileEducation` SHALL manage entries with `{school,
degree?, field?, startMonth, endMonth?, description?}`, with the same replace and delete rules as experience.

#### Scenario: Ongoing study
- **WHEN** the user adds education at `"NTNU"` with start `2024-08` and no end
- **THEN** the entry is saved with `endMonth: null`

### Requirement: Certification entries
`addProfileCertification` / `updateProfileCertification` / `deleteProfileCertification` SHALL manage entries with
`{name, issuer?, issuedMonth?, expiresMonth?, url?}`, with the same replace and delete rules as experience. Expires
before issued SHALL fail with `InvalidProfileError`.

#### Scenario: Add certification
- **WHEN** the user adds `"AWS Certified Developer"` issued `2023-06`
- **THEN** the entry is saved with issuedMonth `2023-06-01`

### Requirement: Project entries
`addProfileProject` / `updateProfileProject` / `deleteProfileProject` SHALL manage entries with `{name, description?,
url?, startMonth?, endMonth?, skillNames}`, with the same replace, delete, and skill tagging rules as experience.

#### Scenario: Project with skills
- **WHEN** the user adds project `"Job Hunt Assistant"` tagged `[".NET", "Vue"]`
- **THEN** the entry has both skills

### Requirement: Entry validation
Required text (employer, title, school, name) SHALL be non-blank. Short text SHALL be at most 200 characters,
descriptions (Markdown) at most 10,000, and highlights at most 20 items of at most 500 characters, with blank items
dropped. URLs SHALL be absolute http/https. Violations SHALL fail with `InvalidProfileError`, changing nothing.

#### Scenario: Blank employer
- **WHEN** the user adds an experience with employer `"  "`
- **THEN** the request fails with `InvalidProfileError`

#### Scenario: Blank highlights dropped
- **WHEN** the user adds highlights `["Led migration", " "]`
- **THEN** the entry has highlights `["Led migration"]`

### Requirement: Unknown entries
Updating or deleting an entry id that doesn't exist, or that belongs to another user, SHALL fail with
`ProfileEntryNotFoundError`.

#### Scenario: Foreign entry
- **WHEN** the guest updates an experience id that belongs to the owner
- **THEN** the request fails with `ProfileEntryNotFoundError` and the owner's entry is unchanged

### Requirement: Entry order
The profile SHALL return experience and education with current entries (no end) first, then by end month descending,
then by start month descending. Projects SHALL use the same order, with undated ones last. Certifications SHALL be ordered
by issued month descending, undated last.

#### Scenario: Current job first
- **WHEN** the profile has a job from 2018–2020 and a current job started 2021
- **THEN** the current job is listed first
