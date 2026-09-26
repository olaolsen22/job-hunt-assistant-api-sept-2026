# Job Hunt Assistant API

Hobby .NET 10 backend (Minimal APIs, EF Core + SQLite, Identity cookie auth) for a Nuxt job-tracker UI.
LLM job-posting parsing via Gemini (free tier) or Ollama, with a background queue + SSE.

## Read first (instead of exploring the codebase)
- `docs/STATUS.md` — what exists right now: endpoints, entities, config keys, milestone progress. **Start here.**
- `docs/PLAN.md` — design and decisions. Read only the section you need.

## Commands
- Build: `dotnet build`
- Test: `dotnet test` (run a single test class with `--filter` while iterating)
- Run: `make run` (or `dotnet run --project src/JobHunt.Api`) → Scalar UI at `/scalar`. `make watch` for hot reload.
- Migration: `make migration name=<Name>`

## Conventions
- Vertical slices: `src/JobHunt.Api/Features/<Feature>/` holds endpoints, request/response records, and entities.
- Each feature exposes `Map<Feature>Endpoints(this IEndpointRouteBuilder)` using `MapGroup("/api/<feature>")`.
- Errors are `ProblemDetails`. Validate with built-in minimal-API validation (DataAnnotations).
- Inject `TimeProvider`, never `DateTime.UtcNow`. Store UTC `DateTime` (SQLite can't ORDER BY `DateTimeOffset`).
- Every user-owned entity has `UserId` and is covered by the global query filter.
- LLM access only through `IChatClient` (Microsoft.Extensions.AI) keyed by provider. Every call logs `LlmUsage`.
- No Ollama container. A missing Ollama is handled with a 503 plus an SSE `parse.failed` event.
- Secrets: user-secrets / env vars only. Never commit keys.

## Code style: simple over clever
- Write the simplest code that works. Readable beats reusable.
- No abstractions (interfaces, base classes, generic helpers, factories, wrappers) until there are 2+ real uses. A test fake counts as a use (e.g. external HTTP services). No repository layer over EF Core.
- Planning for later is fine when it's on the roadmap (PLAN.md §9 "After MVP"): shape the schema and boundaries so those features fit without rewrites. Don't build abstractions for things that aren't on the roadmap.
- Data model: relational, with real FKs and navigation properties. Expect many related tables. Use JSON columns only for leaf data that's never filtered, joined, or matched.
- No defensive code for impossible cases. Only catch exceptions you actually handle.
- Comments explain *why*, never *what*.
- Change only what the task needs. No drive-by refactors or renames.
- Use framework built-ins before adding a package.
- Delete dead code; don't comment it out.

## Commits
- Atomic: one logical change per commit. Don't mix features, refactors, and formatting.
- Subject: imperative, ≤ 50 chars, no trailing period (e.g. `Add application status history`).
- Body: 1–3 lines of *why*.
- Commit only when asked.
- **Record the why at change time**, not commit time: after each change, append one line to `.claude/commit-notes.md` (`- <area>: <what> — <why>`). When committing, build messages from it, then remove the lines you used. This survives long chats, compaction, and `/clear`.

## Working rules (token budget: personal plan)
- Don't spawn subagents unless the user asks or the task clearly matches `dotnet-expert` / `db-architect`.
- Prefer targeted reads (Grep, then a line range) over whole-file reads. Never read `bin/`, `obj/`, or `Migrations/*.Designer.cs`.
- Answer style: direct, like pre-2020 StackOverflow/GitHub. Answer first, no preamble, no recap, no closing offers. Code over prose.
- Show changed snippets, not whole files.
- After finishing a milestone or a notable feature, run `/log-progress` to update `docs/STATUS.md`.
- The owner is a senior frontend engineer (Vue/Nuxt) learning backend, and last used .NET Framework 4. When a backend or modern-.NET concept first comes up, explain *why it works that way* in 1–2 lines, with a frontend analogy if one fits. Skip HTTP/JSON/REST basics.
