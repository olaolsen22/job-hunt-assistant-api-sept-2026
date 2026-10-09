# Tasks

## 1. Schema and seeding

- [x] 1.1 Add `Features/Profile` entities and `IEntityTypeConfiguration`s per design.md (`Profile.cs`, `ProfileEntries.cs`, `ProfileSkill.cs`): `UserId` on `Profile` (unique) and on the four entry tables, composite PKs on join/level tables, JSON for `Links`/`Highlights`, cascades from `AppUser`/`Profile`/entries, `NoAction` to `Skill`/`JobRole`, `SetNull` to `Location`. Add `DbSet`s and global query filters; verify `dotnet build` passes
- [x] 1.2 Generate `make migration name=AddProfile` (backs up the DB); verify the migration only creates tables and indexes (no changes to existing tables)
- [x] 1.3 Make `LookupSeeder` add an empty `Profile` for each user that lacks one; verify a new test: after startup, owner and guest each have exactly one profile, and a second seeder run adds none

## 2. Profile query and basics

- [x] 2.1 Add `ProfileQueries.profile` (`[UseFirstOrDefault]` + `[UseProjection]`) and `Profile.targetLocations`; verify `ProfileTests`: an empty profile comes back for a fresh user, guest doesn't see owner data, and `saveLocation(isTarget: true)` shows in `targetLocations`
- [x] 2.2 Add `updateProfile` with `InvalidProfileException` + validation (lengths, `MailAddress.TryCreate`, links ≤10 with http/https URLs, trimming, blank → null) and location through `LocationLookup.FindOrCreateAsync` (null clears, `isTarget` untouched); verify tests for every `user-profile` basics/location scenario, including `InvalidLocationError`

## 3. Preferences

- [x] 3.1 Add `updateProfilePreferences` (full replace of the role/work-mode/employment-type joins, roles found or created by name, repeats collapsed; salary ≥0, ISO 4217 currency set built once from `RegionInfo`, uppercase, required with salary; notice 0–12); verify tests for every preferences scenario, including a new role showing in `jobRoles` with `listingCount: 0`

## 4. History entries

- [x] 4.1 Add a month-normalizing helper and `ProfileEntryNotFoundException`; add/update/delete for experience with skill tagging by name (found or created, repeats collapsed, update replaces tags) and entry validation (required text, lengths, highlights ≤20 with blanks dropped); verify `ProfileEntryTests`: month normalization, end before start, same month allowed, employer not creating a company, tag scenarios, validation, foreign id → `ProfileEntryNotFoundError`
- [x] 4.2 Add add/update/delete for education, certification (expires ≥ issued), and project (skill tagging, URL validation); verify tests for each `profile-history` scenario for these kinds
- [x] 4.3 Sort nested entry lists per the spec (current first, end desc, start desc; undated projects/certs last); verify an order test with a current job and two past jobs, and a certification with no date

## 5. Skills and languages

- [x] 5.1 Add `setProfileSkill` / `removeProfileSkill` (upsert by normalized name, level 1–5, remove keeps the lookup and tags) and skill ordering (level desc, name); verify `ProfileSkillTests` for each set/remove/order scenario
- [x] 5.2 Add the `experienceMonths` DataLoader (load tagged experience ranges per skill, merge overlapping month intervals, a current entry runs to the current month via `TimeProvider`, projects ignored); verify tests: single job 12, overlap 9, untagged 0, project only 0, and a current job counted to a fixed fake time
- [x] 5.3 Add `setProfileLanguage` / `removeProfileLanguage`; verify tests for add, replace level, and remove missing → `ProfileEntryNotFoundError`

## 6. Docs and final check

- [x] 6.1 Update `docs/PLAN.md`: replace the §4 `Cv` block with the profile model, the §7 `cvs`/`create/update/deleteCv` lines with the profile query/mutations, and rename M3 to "Profile"; verify no "planned (M3)" CV lines remain
- [x] 6.2 Regenerate `schema.graphql`, run `/log-progress` for `docs/STATUS.md` (entities, mutations, errors, migration, decision: one master profile instead of several CVs), and append the why lines to `.claude/commit-notes.md`; verify `SchemaTests` passes
- [x] 6.3 Run `dotnet test` and `openspec validate add-user-profile`; verify both pass
