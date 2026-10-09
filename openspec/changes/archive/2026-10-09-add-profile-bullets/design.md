# Design

## Context

See proposal.md for why. `add-user-profile` (built, not yet archived or committed) put free text on entries as
`Description` (experience, education, project) and a JSON `Highlights` (experience only). Its `design.md` kept
highlights as JSON because they were "never matched". Referring to a bullet from a future per-listing draft changes
that: the project rule says JSON is for leaf data that is never filtered, joined, or matched, and a draft is a join.

The `AddProfile` migration may already be applied to the owner's `jobhunt.db`, so this is a new migration on top of
it, not an edit of it.

## Goals / Non-Goals

**Goals:**
- A bullet id that survives edits and reordering.
- One id space for all bullet kinds, so a later draft table needs one FK, not four.

**Non-Goals:**
- Listing drafts, selection, AI suggestions.
- Tags on bullets, Markdown in bullets, bulk import of bullets.

## Decisions

### One `ProfileBullet` table with four nullable FKs

```
ProfileBullet   Id, UserId, Text, Position (int, 0-based),
                ExperienceId? | EducationId? | CertificationId? | ProjectId?   (exactly one set, cascade)
```

- Alternative: one bullet table per entry kind (`ProfileExperienceBullet`, …). Rejected: a draft would need four FKs
  or a kind + id pair with no real FK, and the shared mutations would need four code paths.
- Alternative: one table with `Kind` + `EntryId` and no FK. Rejected by the project rule: real FKs and navigation
  properties.
- The cost is a `CHECK` constraint: exactly one of the four FKs is non-null
  (`(ExperienceId IS NOT NULL) + (EducationId IS NOT NULL) + … = 1`). SQLite supports it through
  `HasCheckConstraint`, and it makes a bad row impossible rather than relying on the mutations.
- Each FK has an index `(FK, Position)`. They are partial, since most rows have a null in three of the four. Cascade
  from the entry, and from `AppUser` through `UserId` (global query filter `UserId == CurrentUserId`).
- `Text` max 500 is enforced in the mutations only (as for the other text limits), not in the column.

### Positions are dense, rewritten by the server

`Position` runs 0..n-1 per entry. Add appends at `n`. Move and delete renumber the entry's bullets in one
`SaveChanges`. An entry has at most 20 bullets, so rewriting them is cheap, and the UI never sees gaps. Alternative:
gap numbering (10, 20, 30) or fractional positions. Rejected: more rules for no benefit at this size.

### Four shared mutations with an entry kind

`addProfileBullet(kind, entryId, text)`, `updateProfileBullet(id, text)`, `moveProfileBullet(id, position)`,
`deleteProfileBullet(id)`. `kind` is a new enum `ProfileEntryKind { Experience, Education, Certification, Project }`
(stored nowhere, only an argument). Add looks the entry up by `kind` + `entryId` with the matching `DbSet`, so a
wrong kind is simply "not found". Alternative: 16 mutations (4 per kind). Rejected: the bodies are identical.

Return shapes: add and update → the `ProfileBullet`; delete → the `id` (`PayloadFieldName = "id"`, like the other
deletes); move → the entry's `[ProfileBullet]` in the new order, since several positions change. The move payload
field is named `bullets` (`PayloadFieldName`), matching the entry field.

Errors: `InvalidProfileError` (blank, too long, more than 20, position out of range) and `ProfileEntryNotFoundError`
(unknown bullet or entry, wrong kind, other user's). Both exist already; the not-found message names "Bullet" or the
entry kind.

### Entries no longer take description or highlights

Removed from `ProfileExperience`, `ProfileEducation`, `ProfileProject` and their add/update inputs. Update never
touches bullets, so editing a job's title can't change bullet ids.

Bullets are a navigation (`List<ProfileBullet> Bullets`) on each entry, loaded with `Include` in `ProfileQueries.LoadAsync`
and sorted by `Position` in C# like the other lists. The entry mutations that return a single entry also `Include`
its bullets, so `updateProfileExperience { profileExperience { bullets { text } } }` works the same as the query. A
resolver per entry (N+1) was the alternative; with the data loaded anyway, `Include` is simpler.

### Migration `AddProfileBullets`

Order matters because data moves before columns drop, all in one migration (SQLite runs it in a transaction):
1. Create `ProfileBullets` with the FKs, indexes and `CHECK`.
2. Copy data with raw SQL, per kind: the description first (position 0), then each highlight in order. Highlights
   are a JSON array, so experience uses `json_each(Highlights)`: its `key` is the array index and gives the order.
   Blank descriptions and blank highlights are skipped (`TRIM(...) <> ''`). Position is then `ROW_NUMBER()` per entry
   so it stays dense when the description or a blank was skipped.
3. Drop `Description` (three tables) and `Highlights`.

`Down` re-adds the columns (nullable / `'[]'`) and drops the bullets table, but does not copy bullet text back, so the
old columns come back empty. Rolling back with data means restoring the `*.db.bak` made by `make migration`. A description longer than 500 characters is copied whole (the limit applies to new text only).

A migration test is not practical in the in-memory test DB (it starts at the latest migration), so the SQL is checked
by hand against a copy of the real DB (see tasks 1.2).

### Files

`ProfileEntries.cs` gets `ProfileBullet` and its configuration. The bullet mutations go in a new
`ProfileBulletMutations.cs`, since `ProfileEntryMutations.cs` is already long. `ProfileEntryKind` lives next to them.

## Risks / Trade-offs

- [Nullable-FK table with a CHECK is less obvious than separate tables] → the constraint makes bad rows impossible,
  and one id space is the point of the change.
- [BREAKING schema: `description`, `highlights` removed] → the profile hasn't shipped in a UI, and the data is copied
  into bullets so nothing is lost. The UI repo must change at the same time.
- [`Down` loses the split between description and bullets] → documented above; the DB backup is the rollback path.
- [UI needs more calls to build an entry (add entry, then add each bullet)] → accepted for stable ids. Bulk add can
  come later if it hurts.
- [20-bullet limit per entry may be tight for long-running jobs] → same limit as before; raise it in the validator if
  it bites.

## Migration Plan

1. `make migration name=AddProfileBullets` (backs up `jobhunt.db` first), then hand-edit the generated file to add
   the data copy between the table creation and the column drops.
2. Try it on a copy of the real DB with a few experience rows holding descriptions and highlights; check the bullets.
3. Restart the API: the migration applies at startup.
4. Rollback: stop the API, restore the `*.db.bak`.
