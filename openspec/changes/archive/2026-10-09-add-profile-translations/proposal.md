# Proposal

## Why

A CV is needed in Norwegian and English, but the profile's text is written once. You want to write a bullet in one
language, translate it later, and know whether the translation still matches. Bullets already have ids, so a
translation can hang off a bullet and be checked against the exact text it was made from. A later auto-translate then
only needs one bullet at a time.

## What Changes

- `Profile` gets `contentLanguage` (`ENGLISH` or `NORWEGIAN`, default `ENGLISH`): the language your own text is
  written in. Set with a new `setProfileContentLanguage` mutation, not `updateProfile`.
- New `ProfileBulletTranslation` rows: one per bullet and language, holding the translated `text`, a copy of the bullet
  text it was made from (`sourceText`), and `isMachine`. The bullet itself is the original.
  `setProfileBulletTranslation` (upsert) and `deleteProfileBulletTranslation`.
- Each bullet lists its `translations`. Each translation has a computed `isStale`: true when the bullet text no longer
  equals `sourceText`.
- New `ProfileTranslation` row per language holding a translated `headline` and `summary`, each with a copy of its
  source and a computed stale flag. `setProfileTranslation` (full replace) and `deleteProfileTranslation`. The profile
  lists its `translations`.
- A translation is only ever in the other language than `contentLanguage`. `contentLanguage` can't be changed while
  any translation exists.
- Deleting a bullet deletes its translations. Moving a bullet moves it in every language (one position).
- `schema.graphql`, `docs/STATUS.md`, `docs/PLAN.md` updated.

Out of scope: calling an LLM to translate (the shape only has to let a later per-bullet auto-translate save with
`isMachine = true`), translating titles, degrees, entry names, reference notes or hobbies, CV generation, per-language
CV rendering, translation UI.

## Capabilities

### New Capabilities
- `profile-content-language`: which language the user's own profile text is written in, and the rule that
  translations exist only in the other one.
- `profile-bullet-translations`: id-linked, staleness-tracked translations of bullets.
- `profile-summary-translations`: per-language translations of the profile headline and summary.

### Modified Capabilities

None as spec deltas: `profile-bullets` (from `add-profile-bullets`) isn't archived into `openspec/specs/` yet, so there
is nothing to modify. This change builds on it.

## Impact

- `Features/Profiles/`: `Profile.cs` (`ContentLanguage`, `Translations`), `ProfileEntries.cs` (`ProfileBullet.Translations`),
  new `ProfileTranslation.cs` and `ProfileTranslationMutations.cs`, `ProfileQueries.cs` (include translations).
- One migration `AddProfileTranslations`: `ContentLanguage` column (default English), two new tables.
- GraphQL: new mutations and fields only; nothing existing changes, so the current UI keeps working.
- Independent of `add-profile-details`; the two can be applied in either order.
