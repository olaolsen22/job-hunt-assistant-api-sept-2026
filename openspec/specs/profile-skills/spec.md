# profile-skills Specification

## Purpose
What the user can do and how well: a level per skill and per language, on the same scales listings use, so listings can
later be matched against the profile.

## Requirements

### Requirement: Set a skill level
`setProfileSkill` SHALL accept `{name, level}` and return the profile skill. The skill SHALL be found or created in the
user's skills (case-insensitive). Level SHALL be 1–5, the same scale as listing skills. Setting a skill the profile
already has SHALL replace its level.

#### Scenario: New skill
- **WHEN** the user sets `{name: "TypeScript", level: 5}`
- **THEN** the profile's skills include TypeScript at level 5

#### Scenario: Replace level
- **WHEN** the user sets `{name: "typescript", level: 4}` and TypeScript is already at 5
- **THEN** the profile has one TypeScript entry, at level 4

#### Scenario: Level out of range
- **WHEN** the user sets `{name: "Go", level: 0}`
- **THEN** the request fails with `InvalidProfileError`

### Requirement: Remove a skill level
`removeProfileSkill` SHALL accept `{skillId}` and return the skill id. It SHALL remove the skill from the profile's
skill list only. The skill lookup row and entry tags SHALL stay. Removing a skill not on the profile SHALL fail with
`ProfileEntryNotFoundError`.

#### Scenario: Tags survive removal
- **WHEN** the user removes Vue from their skills and an experience is tagged Vue
- **THEN** the experience is still tagged Vue

### Requirement: Months of experience per skill
Each profile skill SHALL expose `experienceMonths`: the number of calendar months, inclusive, covered by experience
entries tagged with that skill. Overlapping entries SHALL be counted once, and a current entry SHALL count up to the
current month (UTC). Projects SHALL NOT count.

#### Scenario: Single job
- **WHEN** one experience from `2020-01` to `2020-12` is tagged Vue
- **THEN** Vue has `experienceMonths: 12`

#### Scenario: Overlapping jobs counted once
- **WHEN** Vue is tagged on jobs `2020-01`–`2020-06` and `2020-04`–`2020-09`
- **THEN** Vue has `experienceMonths: 9`

#### Scenario: Untagged skill
- **WHEN** a profile skill is not tagged on any experience
- **THEN** it has `experienceMonths: 0`

#### Scenario: Project only
- **WHEN** a skill is tagged only on a project
- **THEN** it has `experienceMonths: 0`

### Requirement: Skill order
The profile SHALL return skills ordered by level descending, then by name.

#### Scenario: Highest level first
- **WHEN** the profile has CSS at 3 and Vue at 5
- **THEN** Vue is listed before CSS

### Requirement: Set a language level
`setProfileLanguage` SHALL accept `{language, level}` with the same `Language` and `CefrLevel` enums as listings, and
return the profile language. Setting a language the profile already has SHALL replace its level. `removeProfileLanguage`
SHALL accept `{language}` and return it. Removing a language not on the profile SHALL fail with `ProfileEntryNotFoundError`.

#### Scenario: Add language
- **WHEN** the user sets `{language: NORWEGIAN, level: C2}`
- **THEN** the profile's languages include Norwegian at C2

#### Scenario: Replace language level
- **WHEN** the user sets English to C1, then to B2
- **THEN** the profile has one English entry, at B2
