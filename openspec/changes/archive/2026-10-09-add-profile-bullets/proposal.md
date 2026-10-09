# Proposal

## Why

The profile's free text is a single `description` string per entry, plus a JSON `highlights` list on experience. You
want to keep every job, degree, certification, and project as a list of bullets and later pick the relevant ones per
listing, or have the AI pick them. A JSON string has no identity, so a draft can't point at "this bullet": editing or
reordering shifts everything, like using the array index as a `v-for` key. Bullets need ids first. Doing it now, before
more profile data is entered, keeps the migration small.

## What Changes

- New `ProfileBullet` table: `Id`, `UserId`, `Text`, `Position`, and a link to exactly one entry (experience,
  education, certification, or project). Bullets keep their id when edited or moved.
- Bullets on all four entry kinds, ordered by `Position`.
- Four shared mutations, with an entry kind argument: `addProfileBullet`, `updateProfileBullet`, `moveProfileBullet`,
  `deleteProfileBullet`.
- Bullets are only edited through those mutations; the entry mutations don't take them.
- **BREAKING**: remove `description` from experience, education and projects, and `highlights` from experience (both
  the schema fields and the mutation inputs). Bullets are the only free text on an entry. The profile-level `summary`
  stays.
- The migration copies existing data into bullets: the old description becomes the first bullet, then each highlight.
  Nothing is lost.
- `schema.graphql`, `docs/STATUS.md`, `docs/PLAN.md` updated.

Out of scope: listing drafts, picking bullets per listing, AI suggestions, skill tags on bullets (tags stay on the
experience or project), Markdown in bullets, a limit change per kind.

## Capabilities

### New Capabilities
- `profile-bullets`: ordered, id-stable bullets on every profile entry, their add/update/move/delete rules, and the
  removal of per-entry `description` and `highlights`.

### Modified Capabilities

None as spec deltas: `profile-history` (from `add-user-profile`) isn't archived into `openspec/specs/` yet, so there
is nothing to modify. Its "description" and "highlights" requirements are superseded by `profile-bullets` (see
design.md).

## Impact

- `Features/Profiles`: new `ProfileBullet` entity, shared bullet mutations, changes to the four entry types and
  their mutations, `ProfileQueries` ordering. One migration (`AddProfileBullets`) with SQL data copy and column drops.
- Tests: `ProfileEntryTests` lose description/highlights cases; new bullet tests.
- Breaking for the UI (separate repo): entry forms lose description and highlights; bullets get their own add, edit,
  reorder and delete controls. The UI must not read `description` or `highlights` after this lands.
- No new packages.
