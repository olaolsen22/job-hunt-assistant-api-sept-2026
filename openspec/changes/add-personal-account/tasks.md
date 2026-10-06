# Tasks

## 1. Data safety

- [x] 1.1 Add `make backup` (copies `$(API)/jobhunt.db` plus `-wal`/`-shm` when present to `jobhunt.<yyyyMMdd-HHmmss>.db.bak*`) and make `migration` depend on it; verify `make backup` creates the files
- [x] 1.2 Add `*.db.bak*` to `.gitignore`; verify `git status` doesn't show the backup files
- [x] 1.3 Add the working rule to `CLAUDE.md`: `jobhunt.db` holds real data, run `make backup` before creating or applying a migration, never delete it to recover from a broken migration (fix forward or restore the backup); verify the rule is under "Working rules"

## 2. Seeded extra accounts

- [x] 2.1 In `AuthSeeder`, seed each `Seed:Users` child (email + password, role `Owner`) via `EnsureUserAsync`, with the owner's password-reset logic pulled into a helper used by both; throw on a blank email or password; verify `dotnet build`
- [x] 2.2 Add tests in `AuthTests` using a factory with `Seed:Users:0:*` set: the extra user logs in and `/api/auth/me` returns role `Owner`; the owner still logs in; the extra user doesn't see the owner's listing; a restart with a changed password accepts the new one and rejects the old one. Verify `dotnet test --filter AuthTests` passes
- [x] 2.3 Document `Seed:Users:N:Email`/`Password` under Config keys in `docs/STATUS.md`; verify the full `dotnet test` passes
