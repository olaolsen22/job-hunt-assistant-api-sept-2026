# Tasks

## 1. Schema and data migration

- [x] 1.1 Add `ProfileBullet` (Id, UserId, Text, Position, four nullable entry FKs with cascade, `HasCheckConstraint` for exactly one FK, partial `(FK, Position)` indexes) and `List<ProfileBullet> Bullets` on experience, education, certification and project; remove `Description` from experience, education and project and `Highlights` from experience; add the `DbSet` and a `UserId` query filter; verify `dotnet build` fails only in the mutations and tests that still use the removed fields
- [x] 1.2 Generate `make migration name=AddProfileBullets` (backs up the DB), then hand-edit it to copy data between creating the table and dropping the columns (description first, then `json_each(Highlights)` in index order, blanks skipped, dense `ROW_NUMBER()` positions); verify on a copy of `jobhunt.db` with an experience that has a description and two highlights, an education with only a description, and an entry with neither: the bullets table holds 3, 1 and 0 rows in the right order, and `PRAGMA foreign_key_check` is clean
- [x] 1.3 Remove `description`/`highlights` from the four add/update mutations and their `Apply` helpers (and the highlight limits), and fix `ProfileEntryTests` accordingly; verify `dotnet test` passes for the existing profile tests

## 2. Reading bullets

- [x] 2.1 `Include` bullets in `ProfileQueries.LoadAsync` and in the entry mutations that return one entry, sorted by `Position` then `Id`; hide `UserId` and the FK properties from the GraphQL type; verify a test: an entry returned from add, update and the `profile` query lists the same bullets in order

## 3. Bullet mutations

- [x] 3.1 Add `ProfileEntryKind` and `addProfileBullet` (append, trim, kind + entry lookup, 500-char and 20-bullet limits); verify `ProfileBulletTests`: append order, all four kinds, wrong kind and foreign entry → `ProfileEntryNotFoundError`, blank/501/21st → `InvalidProfileError`
- [x] 3.2 Add `updateProfileBullet` and `deleteProfileBullet` (delete renumbers the rest); verify tests: edit keeps id and position, delete closes the gap, unknown or foreign id → `ProfileEntryNotFoundError`, deleting an entry removes its bullets
- [x] 3.3 Add `moveProfileBullet` returning the entry's bullets (renumber 0..n-1, range check); verify tests: move up, move to the end, out of range → `InvalidProfileError` with the order unchanged, positions stay consecutive after a sequence of moves
- [x] 3.4 Cover identity; verify a test that updating an experience's fields leaves its bullets and ids unchanged

## 4. Docs and final check

- [x] 4.1 Update `docs/PLAN.md` §4 (bullets replace description/highlights) and `docs/STATUS.md` via `/log-progress` (entity, mutations, **BREAKING** removals, migration `AddProfileBullets`, decision: bullets are rows with one id space), regenerate `schema.graphql`, and append the why to `.claude/commit-notes.md`; verify `SchemaTests` passes
- [x] 4.2 Run `dotnet test` and `openspec validate add-profile-bullets`; verify both pass
