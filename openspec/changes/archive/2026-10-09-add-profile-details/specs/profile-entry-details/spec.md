# Spec Delta

## Purpose

Extra detail on profile history entries: where a job, study or project was, and keeping the profile's skills list complete when skills
are tagged on an experience or project.

## ADDED Requirements

### Requirement: Entry location
A profile experience, education and project SHALL each have an optional `location`, set by their add and update
mutations using the same location input and shared locations as the profile's own location. An update SHALL replace
it, so leaving it out clears it. Deleting the location itself SHALL clear it on the entry, not delete the entry.

#### Scenario: Add with location
- **WHEN** the user adds an experience with location Oslo, Norway
- **THEN** the experience returns that location

#### Scenario: Update clears it
- **WHEN** the user updates the experience without a location
- **THEN** its location is null

#### Scenario: Education and project
- **WHEN** the user adds an education and a project with a location
- **THEN** each returns that location

### Requirement: Tagged skills appear in the profile skills
When an experience or project is added or updated with skill names, every skill not yet in the profile's skills SHALL
be added with level 1. A skill already in the profile's skills SHALL keep its level. Removing a tag from an entry, and
`removeProfileSkill`, SHALL NOT change the other side.

#### Scenario: New tag adds a skill
- **WHEN** the user tags "Vue" on an experience and the profile's skills don't contain Vue
- **THEN** the profile's skills contain Vue at level 1

#### Scenario: Existing level kept
- **WHEN** the profile has Vue at level 4 and the user tags "vue" on a project
- **THEN** the profile's Vue is still level 4 and there is one Vue entry

#### Scenario: Untag keeps the skill
- **WHEN** the user updates an experience without the "Vue" tag
- **THEN** the profile's skills still contain Vue

### Requirement: Existing tags are backfilled
The migration SHALL add every skill already tagged on an experience or project to the same profile's skills at level 1
unless it is already there.

#### Scenario: Backfill
- **WHEN** the migration runs on a profile with a project tagged "Docker" and no Docker profile skill
- **THEN** the profile's skills contain Docker at level 1, and existing levels are unchanged
