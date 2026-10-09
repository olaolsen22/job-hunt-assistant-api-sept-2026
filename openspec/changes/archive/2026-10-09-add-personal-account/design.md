# Design

## Context

`AuthSeeder` runs at startup after migrations. It ensures the roles exist, creates the owner and guest if missing, and
re-applies the owner's password from config. Roles aren't checked anywhere else in `src`, so a second `Owner` gets
exactly what the owner has. Every user table is scoped by the global query filter, and `LookupSeeder` already seeds
starter lookups per user, so a new user starts with its own empty data.

The DB is empty, so no existing data has to move between accounts.

## Goals / Non-Goals

**Goals:**
- A second, real account next to the test owner, using config only.
- Make it hard to lose real data in `jobhunt.db` during development.

**Non-Goals:**
- Registration, password change in the app, email confirmation (PLAN §9, public launch).
- A separate role for the real account, or any role-based authorization.
- Docker: no compose passthrough for `Seed:Users` and no backups for the volume (`/data/jobhunt.db`). Everything runs
  locally with `make run`/`make watch`. An optional compose variable would show up as a blank entry and fail the
  blank-value check. Scheduled or off-machine backups are out of scope too.
- Removing the guest or building the guest AI cap. On a Tailscale-only machine, only the owner's devices can reach it.

## Decisions

- **`Seed:Users` array instead of a single `Seed:UserEmail`/`Seed:UserPassword` pair.** It's the same code size (one
  `foreach` over `GetChildren()`), and you can add a third account without another change. Indexed keys work with
  user-secrets (`Seed:Users:0:Email`) and env vars (`Seed__Users__0__Email`).
- **Role `Owner`, not a new `User` role.** No code checks roles, so a new role would only be a label. PLAN §9 says
  "authorize by role/policy, never by a specific email". When owner-only rights exist, adding a role and changing one
  config entry is enough.
- **Password re-applied from config, same as the owner.** Config stays the one source of truth until there's an in-app
  password change. The password reset block moves into a small helper used by the owner and every extra user (2+ real
  uses).
- **No new required config.** A missing `Seed:Users` section means no extra users. A present entry with a blank email
  or password throws at startup, like the owner's settings do, so a typo doesn't quietly leave you without an account.
- **Backup with `make backup` (`cp` to `jobhunt.<timestamp>.db.bak`), and `migration: backup`.** Migrations run on
  startup, so a backup taken when the migration is created is always taken before it's applied. Alternatives:
  SQLite `.backup` (needs the `sqlite3` CLI, which isn't installed), and a separate DB file for real data (rejected:
  the test and real accounts are meant to share one running app).
- **Assumption: real data lives in the dev `jobhunt.db` (`make run` / `make watch`), not the Docker volume.** That's
  the owner's normal run mode.

## Risks / Trade-offs

- [`cp` while the app is running can copy a DB mid-write, and recent changes may still be in `jobhunt.db-wal`] → copy
  `-wal`/`-shm` too when they exist. The backup runs at `make migration` time, when the app is usually idle.
- [Backups pile up] → they're small and gitignored. Clean them up by hand.
- [Weak password in user-secrets] → acceptable: Tailscale-only, not public. It's still never committed.
- [Config resets a password changed some other way] → no other way exists yet. Switch to "create only" when one does.
