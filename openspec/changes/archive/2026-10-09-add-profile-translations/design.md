# Design

## Context

See proposal.md for why. `ProfileBullet` (from `add-profile-bullets`) has a stable id, text, and a single `Position`.
`Profile.Headline` and `Summary` are plain strings, replaced by `updateProfile`. The existing `Language` enum is
`{ English, Norwegian }` (used by `ProfileLanguage` and listings), so it already is the supported set.

## Goals / Non-Goals

**Goals:**
- A translation tied to its original by id, with a way to see that it is out of date.
- A shape a later per-bullet auto-translate can write to without changes.

**Non-Goals:**
- Translating or calling an LLM now; translating other fields; deciding which language a CV uses.

## Decisions

### The original stays where it is; translations are child rows
```
ProfileBulletTranslation  Id, UserId, BulletId (cascade), Language, Text, SourceText, IsMachine   unique (BulletId, Language)
ProfileTranslation        Id, UserId, ProfileId (cascade), Language, Headline?, Summary?,
                          SourceHeadline?, SourceSummary?, IsMachine                              unique (ProfileId, Language)
```
Rejected: a second bullet row per language with a `TranslationOf` FK. It breaks "one id per bullet", needs positions
in two places, and reordering would have to be mirrored. Both tables get a `UserId` filter like the other profile rows.

### Staleness is computed from a source copy, not a timestamp or flag
`isStale = bullet.Text != SourceText`. Nothing has to be updated when the original is edited, so it can't drift, and
the UI can show the old source next to the new text. EF fixes up `translation.Bullet` when translations are loaded
through the bullet, so the check needs no extra query. For `ProfileTranslation` the same applies per field, and an
absent translated field is never stale.

### `contentLanguage` is a separate mutation and frozen while translations exist
It is a column with default `English`, set by `setProfileContentLanguage`, so `updateProfile` (a full replace, shared
with `add-profile-details`) isn't touched. Changing it relabels all existing text, which makes any existing
translation wrong (its source would be in the target language). The simplest consistent rule is to refuse while any
translation exists; the user deletes them first. Alternatives: auto-delete (silent data loss), or keep and ignore rows
in the new language (hidden state).

### `isMachine` is just a flag the caller passes
Setting by hand leaves the default `false`; the later auto-translate passes `true`. Replacing a machine translation
by hand clears it because the caller sends no flag. No LLM code or `LlmUsage` is involved yet.

### Two mutation pairs, not one generic
Bullet translations and profile translations differ in key and fields. Two small files' worth of mutations, no shared
base (2 uses, different shapes).

## Risks / Trade-offs

- [Source copy doubles the stored text] → Bullets are at most 500 characters and 20 per entry; negligible.
- [Whitespace-only edits to a bullet show the translation as stale] → Text is trimmed on save, so only real changes differ.
- [A user can't flip `contentLanguage` quickly] → Intended; the error says to delete translations first.

## Migration Plan

`make migration name=AddProfileTranslations` (backs up the DB). It adds `ContentLanguage` with default `English`
(stored as text like the other enums) and two empty tables; no data step. Verify on a copy of `jobhunt.db` that existing
profiles read `ENGLISH`. Rollback: restore the `*.db.bak`.
