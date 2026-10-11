# Job Hunt Assistant API

Personal .NET 10 job-tracker backend and GitHub portfolio project: Minimal APIs,
EF Core + SQLite, Identity cookie auth, Hot Chocolate GraphQL, and LLM parsing
through Microsoft.Extensions.AI. Optimize for readable, idiomatic code and useful
features within a single API project. Add operational complexity only for a
concrete need. User isolation, input validation, secret handling, and preserving
real data are required even for this personal app.

## Context and scope
- API only; the Nuxt UI lives in `../job-hunt-assistant-ui-sept-2026`. For a
  misplaced UI request, explain the repo boundary before editing. Explicit owner
  instructions can change scope.
- `docs/STATUS.md` describes current behavior; `docs/PLAN.md` holds design and
  roadmap decisions. Read relevant sections when needed, once per task, and
  reread only after changes or compaction. For small edits, start at the affected
  file. Code is authoritative when documentation disagrees.
- Use targeted searches (`rg` or Grep), then relevant line ranges. Batch
  independent reads. Skip `bin/`, `obj/`, `Migrations/*.Designer.cs`, and `*.db`;
  read model snapshots only for migration debugging.

## Commands and verification
- Build: `dotnet build`. Test: `dotnet test`; use `--filter` while iterating.
- Run only when asked: `make run` or `make watch`; Scalar is at `/scalar`.
- Migration: `make migration name=<Name>` (backs up first).
- Test observable behavior: success plus relevant validation, user isolation,
  and failure cases. Match verification to the change; skip runtime tests for
  documentation-only edits. `dotnet test` builds too, so avoid a separate build
  unless needed. Run the full suite once before finishing runtime changes;
  repeat only after new edits or unresolved failures.
- The owner usually has the API running. Do not start or stop it without a
  request. If a live probe is necessary, ask first, use a separate port and DB,
  and stop only that PID. Never use `pkill` or `killall`.
- `src/JobHunt.Api/jobhunt.db` contains real data. Run `make backup` before
  creating or applying migrations. Review migration `Up()` for data loss;
  never delete the DB to recover: fix forward or restore a backup.

## Implementation conventions
- Vertical slices: `src/JobHunt.Api/Features/<Feature>/` contains its API types,
  records, entities, and `IEntityTypeConfiguration<T>` configurations.
- REST only for auth, health, and document download: `MapGroup`, typed results,
  built-in DataAnnotations validation, `ProblemDetails` errors.
- Domain data uses GraphQL `[QueryType]` / `[MutationType]`, queryable paging,
  projection/filtering/sorting where suitable, and typed mutation errors.
  Never expose the same operation through both REST and GraphQL.
- Inject `TimeProvider`. Store UTC `DateTime` for SQLite sorting, enums as
  strings. User-owned entities need `UserId`, its index, and the global filter.
- Use real FKs and navigation properties. JSON is for leaf data never queried
  relationally. Add indexes for actual queries; account for SQLite table rebuilds.
- LLM calls use provider-keyed `IChatClient` and log `LlmUsage`. No Ollama
  container; unavailable providers fail the parse job through its existing flow.
- Secrets belong in user-secrets or environment variables; never commit keys.

## Keep work small
- Prefer framework built-ins and straightforward code. Add abstractions when
  repeated use or an external boundary justifies them; a test fake counts.
  No repository layer over EF Core or MediatR by default.
- Follow established patterns. Consider relevant roadmap constraints without
  building future features. Avoid unrelated refactors, renames, and packages.
- Validate external input; catch exceptions only when handling them. Comments
  explain non-obvious reasons. Remove dead code.
- Use OpenSpec when requested or continuing an existing change. Keep artifacts
  proportional to scope; small fixes need no new proposal by default.
- Use specialist guidance for non-trivial .NET or data-model work. Spawn
  subagents only when requested. Skip formal plans for straightforward edits.
- After notable behavior changes, use `log-progress` to update relevant STATUS
  sections; do not rewrite unrelated documentation.

## Communication and commits
- Be direct and concise: outcome, meaningful changes, validation, limitations.
  Show snippets only when useful. Explain a new backend/.NET concept in 1–2
  lines for a senior Vue/Nuxt developer learning modern .NET; skip HTTP basics.
- Commit only when asked. One logical change per commit; imperative subject
  ≤50 chars. Body explains why, wrapped at 72 chars. For owner requests, use
  `Owner requested <x>, so <goal>:` with concise change bullets.
- Record one rationale per logical change in `.claude/commit-notes.md`:
  `- <area>: <what> — owner requested: <why>` for owner requests. Reuse these
  shared notes in commit messages, then remove only the consumed lines.
