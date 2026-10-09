# Proposal

## Why

The app is going into private use: it runs locally behind Tailscale, not on a public server. The seeded owner account
(`Seed:OwnerEmail`) stays as the test account for random data, and real job-hunt data needs its own account. There is no
registration (closed on purpose, PLAN §3), so the only way to get another account is to seed it from config.

From now on, real data lives in the same `jobhunt.db` that development runs against, and migrations apply automatically
at startup. A bad migration or a "delete the DB and start over" reset would lose real data, so backups have to become
part of the workflow.

## What Changes

- Seed any number of extra password accounts from config (`Seed:Users:N:Email`, `Seed:Users:N:Password`), role `Owner`.
  Created at startup if missing. Their password is re-applied from config on every startup, the same way as the owner's.
- Existing accounts (owner, guest) are never changed by this. No `Seed:Users` entries means no change in behavior.
- `make backup` copies `jobhunt.db` to a timestamped `*.db.bak`, and `make migration` runs it first. `*.db.bak` is
  gitignored.
- `CLAUDE.md` gets a working rule: `jobhunt.db` holds real data. Back it up before creating or applying a migration, and
  never delete it to recover from a broken migration.

## Capabilities

### New Capabilities
- `seeded-accounts`: user accounts created from configuration at startup (owner, guest, extra users), and how their
  passwords are managed.

### Modified Capabilities

## Impact

- `Features/Auth/AuthSeeder.cs`, `tests/.../Features/Auth/AuthTests.cs`, `Makefile`, `.gitignore`, `CLAUDE.md`, `docs/STATUS.md` (config keys).
- No new packages, no migration, no GraphQL/REST contract change. The UI is unaffected: it already logs in by email and
  password.
