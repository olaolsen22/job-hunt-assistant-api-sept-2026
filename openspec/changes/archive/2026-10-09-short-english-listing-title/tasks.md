# Tasks

## 1. Prompts

- [x] 1.1 In `ListingParsePromptQueries.cs`, change the `title` output comment in both prompts from "exactly as written in the ad, original language" to a short English title (e.g. `// short job title in natural English`). Verify: grep shows no "original language" for `title`.
- [x] 1.2 Rewrite the `title` rule in both prompts: source field (Stillingstittel/Job title, else heading), translate to natural English, keep seniority/specialty, drop company, location, workload/employment type, slogans and bilingual duplicates; one example (e.g. "Senior systemutvikler .NET – Oslo, 100 % fast" → "Senior .NET Developer"). Verify: read both prompts.
- [x] 1.3 Bump `Version` 7 → 8 and `QuickVersion` 4 → 5. Verify: constants updated.

## 2. Tests and docs

- [x] 2.1 Update `ListingParsePromptTests` title assertions: both prompts contain the English-title rule and don't contain "original language" for title. Verify: `dotnet test --filter ListingParsePromptTests` passes.
- [x] 2.2 Run full `dotnet test`. Verify: all green.
- [x] 2.3 Append a line to `.claude/commit-notes.md` (owner requested: short English listing titles).
