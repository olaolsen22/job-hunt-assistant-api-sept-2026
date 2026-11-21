# Codex instructions

Job Hunt Assistant API

Personal .NET 10 job-tracker backend and GitHub portfolio project: Minimal APIs,
EF Core + SQLite, Identity cookie auth, Hot Chocolate GraphQL, and LLM parsing
through Microsoft.Extensions.AI. Optimize for readable, idiomatic code and useful
features within a single API project. Add operational complexity only for a
concrete need. User isolation, input validation, secret handling, and preserving
real data are required even for this personal app.

## Context and scope
- Project Codex defaults live in `.codex/config.toml`; reusable skills live in
  `.agents/skills/`. Keep model choice and credentials in personal settings.
- Preserve existing uncommitted work. Change only files needed for the task;
  never reset or discard unrelated changes.
- API only; the Nuxt UI lives in `../job-hunt-assistant-ui-sept-2026`. For a
  misplaced UI request, explain the repo boundary before editing. Explicit owner
  instructions can change scope.
- `docs/STATUS.md` describes current behavior; `docs/PLAN.md` holds design and
  roadmap decisions. Read relevant sections when needed, once per task, and
  reread only after changes or compaction. For small edits, start at the affected
  file. Code is authoritative when documentation disagrees.
- Use targeted searches (`rg` or Grep), then relevant line ranges. Batch
  independent reads. Skip `.local/` backups unless explicitly needed, `bin/`,
  `obj/`, `Migrations/*.Designer.cs`, and `*.db`;
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

## New feature workflow
- For a new feature, ask the owner for the user flow it serves. If changes are
  requested and no flow exists, ask before coding.
- Brainstorm with the owner: give a brief overview of the flow and the API
  shape it needs (queries, mutations, errors, data). Wait for an explicit go.
- Consult `$dotnet-expert` for API design and `$db-architect` for data-model
  changes before building.
- Write tests first, then implement. Keep tests light: the main success path
  plus the validation, user isolation, and failure cases that matter.

## Communication and commits
- Be direct and concise: outcome, meaningful changes, validation, limitations.
  Show snippets only when useful. Explain a new backend/.NET concept in 1–2
  lines for a senior Vue/Nuxt developer learning modern .NET; skip HTTP basics.
- UI instructions: quick and short. The UI repo reads `schema.graphql` and can
  generate types when needed, so list only what changed and any non-obvious
  behavior; never restate the schema. Always output them as Markdown inside a
  fenced code block (use a longer fence if the content has code fences) so they
  can be copied as-is.
- The history is public and read by other developers; keep it clean.
- Log each logical change in `.local/changelog.md` (gitignored) when it is
  finished, newest first:
  ```md
  ## YYYY-MM-DD · <area>: <what>
  - Goal: <the request's goal, neutral and concise>
  - Why: <problem it solves>
  - Files: <main files>
  - Branch: <type>/<slug>
  - Status: uncommitted
  ```
- Branches: `development` is the integration branch; `main` is for publishing
  and only gets `development` merged in when the owner asks. Each feature or
  fix gets a short-lived branch off `development` named `<type>/<slug>`
  (`feat/listing-shortlist`, `fix/quick-add-limit`), holding one feature in
  one or more small commits. Before starting a change on the wrong branch,
  flag it and offer to create the right one.
- Finish a branch: on the owner's yes, open a GitHub PR into `development`
  (`gh pr create`, short description of what and why), merge it with a merge
  commit, and delete the branch.
- Commit boundary: when a logical change is done and its tests pass, stop and
  propose the commit subject; commit only on the owner's yes. Before starting
  a different change while another is uncommitted, flag it first.
- On "commit", group uncommitted changes by changelog entry, one commit each,
  then set each entry's `Status` to its short hash.
- Message: Conventional Commits subject `<type>(<scope>): <imperative>` ≤50
  chars (`feat`, `fix`, `refactor`, `test`, `docs`, `chore`). Body: the why as
  product reasoning in 1–3 lines, wrapped at 72. No prompts, "owner
  requested", or AI wording in commits.

## Skills

OpenSpec uses the existing `openspec/` root. Use these Codex skills:

| Action | Codex skill |
|---|---|
| propose | `$openspec-propose` |
| explore | `$openspec-explore` |
| apply | `$openspec-apply-change` |
| update | `$openspec-update-change` |
| sync | `$openspec-sync-specs` |
| archive | `$openspec-archive-change` |

Use the matching skill when invoked. `/log-progress` maps to `$log-progress`.
Use `$dotnet-expert` or `$db-architect` for relevant non-trivial work; they are
local guidance, not subagent configurations. Load only the relevant skill,
reuse context already read, and follow shared verification rules rather than
running duplicate builds. Keep generated OpenSpec skills managed by OpenSpec.
