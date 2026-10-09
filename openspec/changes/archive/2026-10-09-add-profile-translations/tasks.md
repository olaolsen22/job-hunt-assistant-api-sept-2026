# Tasks

## 1. Content language

- [x] 1.1 Add `Profile.ContentLanguage` (`Language`, default English) and `setProfileContentLanguage` (rejected while any bullet or profile translation exists); generate `make migration name=AddProfileTranslations` and verify on a copy of `jobhunt.db` that existing profiles read `ENGLISH`
- [x] 1.2 Cover it in `ProfileTests`; verify tests: default `ENGLISH`, change with no translations, blocked by an existing translation, same value succeeds

## 2. Bullet translations

- [x] 2.1 Add `ProfileBulletTranslation` (fields, `UserId` filter and FK, cascade from the bullet, unique `(BulletId, Language)`, `DbSet`), `ProfileBullet.Translations`, a computed `IsStale`, and include translations ordered by language in `ProfileQueries.LoadAsync` and the bullet mutations' results; extend the migration; verify `dotnet build` and that the table exists
- [x] 2.2 Add `setProfileBulletTranslation` and `deleteProfileBulletTranslation`; verify `ProfileBulletTranslationTests`: create, replace keeps the id, machine flag cleared by a hand edit, same language as content, blank/501 characters, foreign or unknown bullet and translation ids
- [x] 2.3 Verify staleness and cascade in tests: editing the bullet makes the translation stale without changing it, setting it again clears the flag, deleting a bullet or its experience deletes the translation, moving a bullet leaves translations unchanged

## 3. Headline and summary translations

- [x] 3.1 Add `ProfileTranslation` (fields, `UserId` filter and FK, cascade from the profile, unique `(ProfileId, Language)`, `DbSet`), `Profile.Translations` with `IsHeadlineStale` / `IsSummaryStale`, and include it in `LoadAsync`; extend the migration; verify `dotnet build` and that the table exists
- [x] 3.2 Add `setProfileTranslation` (full replace, source copies stored, field limits from the source fields) and `deleteProfileTranslation`; verify `ProfileTranslationTests`: translate both, replace clears a field, no source field, nothing given, same language as content, unknown language row on delete, per-field staleness after `updateProfile`

## 4. Docs

- [x] 4.1 Update `docs/PLAN.md` §4 (translation tables, content-language rule, and a §9 note that CV generation takes a target language and later auto-translate writes with `isMachine`), run `/log-progress` for `docs/STATUS.md` (fields, mutations, migration `AddProfileTranslations`, decision: staleness is computed from a source copy), regenerate `schema.graphql`, and append the why to `.claude/commit-notes.md`; verify `SchemaTests` passes
- [x] 4.2 Run `dotnet test` and `openspec validate add-profile-translations`; verify both pass
