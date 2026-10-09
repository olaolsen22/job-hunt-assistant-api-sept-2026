# Tasks

## 1. Personal details and typed links

- [x] 1.1 Add `DateOfBirth`, `Nationality`, `WorkAuthorization` (with the allowlist comment), `Hobbies` (JSON) and `ProfileLink.Kind` (string-stored enum) to `Profile`; verify `dotnet build` fails only where `ProfileLink` / `updateProfile` are used
- [x] 1.2 Extend `updateProfile` and `ProfileLinkInput`: new args, future-date, hobby and link rules (host check, label for `OTHER`, one per kind); verify `ProfileTests` cover set/clear, future date, hobby dedupe/limits, wrong host, `OTHER` without label
- [x] 1.3 Generate `make migration name=AddProfileDetails` for this group, hand-edit it to rewrite existing `Links` JSON with `Kind` `Other`; verify on a copy of `jobhunt.db` that a link labelled "Blog" keeps its label with kind `Other`

## 2. References

- [x] 2.1 Add `ProfileReference` (fields, cascade, `UserId` filter, `(ProfileId, Position)` index, `DbSet`) and `Include` it, ordered, in `ProfileQueries.LoadAsync`; extend the migration; verify `dotnet build` and the migrated table exists
- [x] 2.2 Add `addProfileReference`, `updateProfileReference`, `moveProfileReference`, `deleteProfileReference` with the validation and 10-item limit; verify `ProfileReferenceTests`: append order, move, delete closes the gap, blank relationship, bad email, foreign/unknown id, out-of-range move leaves order unchanged

## 3. Experience location and skill sync

- [x] 3.1 Add `Location` (optional FK, `SetNull`) to `ProfileExperience`, `ProfileEducation` and `ProfileProject` and the `location` argument on their add/update mutations, using `LocationLookup`; extend the migration; verify tests for add with location on each kind, update clears it, deleting the location keeps the entry
- [x] 3.2 In the experience and project `ApplyAsync`, add missing tagged skills to `ProfileSkills` at level 1; verify tests: new tag adds, existing level kept with case-insensitive name, untag keeps the skill, `removeProfileSkill` leaves tags
- [x] 3.3 Add the skills backfill SQL to the migration; verify on a copy of `jobhunt.db` with a project tagged "Docker" and no profile skill: Docker is added at level 1, existing levels are unchanged, and `PRAGMA foreign_key_check` is clean

## 4. Docs

- [x] 4.1 Update `docs/PLAN.md` §4 and §9 note on the AI allowlist, run `/log-progress` for `docs/STATUS.md` (fields, mutations, **BREAKING** link `kind`, migration `AddProfileDetails`), regenerate `schema.graphql`, and append the why to `.claude/commit-notes.md`; verify `SchemaTests` passes
- [x] 4.2 Run `dotnet test` and `openspec validate add-profile-details`; verify both pass
