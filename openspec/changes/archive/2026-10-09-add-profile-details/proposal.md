# Proposal

## Why

The profile is the master copy that CVs will be generated from, and it still lacks details a CV needs: date of
birth, nationality, work authorization, references, hobbies, and typed links (GitHub, LinkedIn). Experience has no
location, and neither do education or projects. Skills tagged on an experience or project don't show in the profile's skills list, so you enter each skill
twice. Doing it now, before more profile data is entered, keeps the migrations small.

## What Changes

- `Profile` gets `dateOfBirth` (date), `nationality` and `workAuthorization` (text), and `hobbies` (list of text).
  All optional, set by `updateProfile`. The first three are sensitive: they are only stored and shown, and a future AI
  CV prompt builder must pick fields from an explicit allowlist. Nothing enforces that now (no AI reads the profile yet).
- Profile links get a `kind` (`GITHUB`, `LINKEDIN`, `PORTFOLIO`, `OTHER`). `label` is only used for `OTHER`. A
  GitHub or LinkedIn link must be on that site's host. **BREAKING**: `kind` is required on every link input.
  Existing links migrate to `OTHER` and keep their label.
- New `ProfileReference` rows (name, relationship, company, email, phone, note, position) with
  `addProfileReference`, `updateProfileReference`, `moveProfileReference`, `deleteProfileReference`.
- Experience, education and project entries get an optional `location`, set by their add/update mutations.
- Tagging a skill on an experience or project adds it to the profile's skills with level 1 if it isn't there yet.
  Untagging or removing a profile skill never changes the other side. The migration backfills skills already tagged.
- `schema.graphql`, `docs/STATUS.md`, `docs/PLAN.md` updated.

Out of scope: a photo, address, enforcing the AI allowlist, location on certifications, deriving skill levels from
experience months, hiding "available on request" references.

## Capabilities

### New Capabilities
- `profile-personal-details`: date of birth, nationality, work authorization, hobbies, and typed profile links.
- `profile-references`: ordered references with add/update/move/delete.
- `profile-entry-details`: entry location, and tagged entry skills appearing in the profile's skills.

### Modified Capabilities

None as spec deltas: the profile specs from `add-user-profile` and `add-profile-bullets` aren't archived into
`openspec/specs/` yet, so there is nothing to modify. This change builds on them.

## Impact

- `Features/Profiles/`: `Profile.cs` (fields, `ProfileLink.Kind`), new `ProfileReference.cs` and
  `ProfileReferenceMutations.cs`, `ProfileEntries.cs` (entry location), `ProfileMutations.cs`,
  `ProfileEntryMutations.cs`, `ProfileQueries.cs` (include references).
- One migration `AddProfileDetails`: new columns, `ProfileReferences` table, `LocationId` on experience, education and project, and a data step
  (links `Kind`, skill backfill).
- GraphQL: `updateProfile` and the experience mutations gain arguments; link input gains required `kind`.
- UI repo needs a follow-up (separate session) for the new fields and `kind`.
