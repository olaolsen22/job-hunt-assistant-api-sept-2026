@AGENTS.md

# Claude Code notes

`AGENTS.md` (imported above) is the shared source of truth for Codex and Claude.
Edit rules there, not here. This file only adds Claude-specific mapping and a
code map for fast navigation.

## Skill mapping
`.claude/skills` is a symlink to `.agents/skills`, so both tools share one copy.
Where AGENTS.md says `$skill`, Claude uses `/skill` (or the Skill tool):
`/openspec-propose`, `/openspec-explore`, `/openspec-apply-change`,
`/openspec-update-change`, `/openspec-sync-specs`, `/openspec-archive-change`,
`/log-progress`, `/dotnet-expert`, `/db-architect`. Load the skill inline; do not
spawn subagents unless asked.

## Code map
- `src/JobHunt.Api/Program.cs` — DI, auth, rate limits, GraphQL setup, REST maps
  (`MapAuthEndpoints`, `MapDocumentEndpoints`, `MapBaseCvEndpoints`, health).
- `src/JobHunt.Api/Features/<Feature>/` — entity + config, `*Queries.cs`,
  `*Mutations.cs`, `*Endpoints.cs` (REST only for Auth/Documents/BaseCvs).
- `src/JobHunt.Api/Data/` — `AppDbContext` (global user filters), seeders, shared
  input/name/sorting helpers, SQLite error mapping.
- `src/JobHunt.Api/Llm/` — provider-keyed `IChatClient`s (`ProviderClients`),
  `LlmExecution`, `LlmUsage`, pricing/catalog options.
- `src/JobHunt.Api/Features/Parsing/` — parse job flow (`ParseJob`,
  `ListingParser`, `ListingPageFetcher`).
- `schema.graphql` — generated schema snapshot, checked by `SchemaTests`; read
  with targeted `rg`, never whole (≈130 KB).
- Tests: `tests/JobHunt.Api.Tests/Features/<Feature>/*Tests.cs` using
  `ApiFactory` (in-memory SQLite, owner/guest/second-owner clients),
  `FakeChatClient`, `FakePageHandler`, `FakeProviderHandler`,
  `GraphQLExtensions`. Filter: `dotnet test --filter FullyQualifiedName~<Feature>`.
- Docs: `docs/STATUS.md` (current), `docs/PLAN.md` (roadmap), feature docs in
  `docs/*.md`; OpenSpec in `openspec/changes/` and `openspec/specs/`.

## Long-running server
Owner standing request (overrides AGENTS.md "do not start or stop"): after
finishing code changes in a task, restart the API once so it runs the new code
and applies pending migrations. Skip for docs-only edits.
Run `make restart`: it stops only this API's watch processes (SIGKILL for
leftovers) and starts a detached `make watch` that outlives the Bash tool.
Check `.local/watch.log` for startup.

## Off-limits
Never read `*.db*` / `*.db.bak*`, `src/JobHunt.Api/.local/` (data-protection
keys), `src/JobHunt.Api/listing-imports/`, `.local/backup-*`, or user-secrets.
`.local/changelog.md` is fine to read and edit.
